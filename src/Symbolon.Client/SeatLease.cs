using System.Diagnostics;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Symbolon.Protocol;
using Symbolon.Protocol.Tracing;

namespace Symbolon.Client;

/// <summary>
/// Active floating seat lease held by the client application.
/// Manages automatic background heartbeats, jitter, grace periods, and disposal.
/// </summary>
public sealed partial class SeatLease : IAsyncDisposable, IDisposable
{
    private readonly HttpClient _http;
    private readonly TimeProvider _time;
    private readonly TimeSpan _heartbeatInterval;
    private readonly TimeSpan _gracePeriod;
    private readonly IReadOnlyDictionary<string, string> _fingerprint;
    private readonly ILogger? _log;
    private readonly CancellationTokenSource _cts = new();
    private Task? _heartbeatTask;

    private long _clientSeq;
    private DateTimeOffset _expiresAt;
    private SeatState _state = SeatState.Active;
    private DateTimeOffset? _graceStartedAt;

    public bool Acquired => _state is SeatState.Active or SeatState.GracePeriod or SeatState.Borrowed;
    public string? Reason { get; }
    public string? LeaseId { get; }
    public string? Token { get; private set; }
    public int SeatNo { get; }
    public IReadOnlyList<string> Entitlements { get; }

    public SeatState State
    {
        get => _state;
        private set
        {
            if (_state != value)
            {
                _state = value;
                StateChanged?.Invoke(this, new SeatStateChangedEventArgs(value));
            }
        }
    }

    public event EventHandler<SeatStateChangedEventArgs>? StateChanged;

    internal SeatLease(
        bool acquired,
        string? reason,
        string? leaseId,
        string? token,
        int seatNo,
        DateTimeOffset expiresAt,
        IReadOnlyList<string> entitlements,
        HttpClient http,
        TimeProvider time,
        TimeSpan heartbeatInterval,
        TimeSpan gracePeriod,
        IReadOnlyDictionary<string, string> fingerprint,
        ILogger? log = null)
    {
        Reason = reason;
        LeaseId = leaseId;
        Token = token;
        SeatNo = seatNo;
        _expiresAt = expiresAt;
        Entitlements = entitlements;
        _http = http;
        _time = time;
        _heartbeatInterval = heartbeatInterval;
        _gracePeriod = gracePeriod;
        _fingerprint = fingerprint;
        _log = log;

        if (acquired)
        {
            _state = SeatState.Active;
            _heartbeatTask = RunHeartbeatLoopAsync(_cts.Token);
        }
        else
        {
            _state = SeatState.Lost;
        }
    }

    public static SeatLease Denied(string reason)
    {
        return new SeatLease(
            acquired: false,
            reason: reason,
            leaseId: null,
            token: null,
            seatNo: -1,
            expiresAt: DateTimeOffset.MinValue,
            entitlements: [],
            http: null!,
            time: TimeProvider.System,
            heartbeatInterval: TimeSpan.Zero,
            gracePeriod: TimeSpan.Zero,
            fingerprint: new Dictionary<string, string>());
    }

    private async Task RunHeartbeatLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            // Calculate jitter: ±10% around heartbeat interval
            int jitterPercent = RandomNumberGenerator.GetInt32(-10, 11);
            double factor = 1.0 + (jitterPercent / 100.0);
            TimeSpan delay = TimeSpan.FromMilliseconds(_heartbeatInterval.TotalMilliseconds * factor);

            try
            {
                await Task.Delay(delay, _time, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            if (ct.IsCancellationRequested) break;

            await PerformHeartbeatAsync(ct).ConfigureAwait(false);
        }
    }

    private async Task PerformHeartbeatAsync(CancellationToken ct)
    {
        if (LeaseId is null) return;

        using var activity = SymbolonTracing.ActivitySource.StartActivity(SymbolonTracing.OpRenew);
        activity?.SetTag(SymbolonTracing.TagLeaseId, LeaseId);

        var renewDto = new RenewRequestDto
        {
            ClientSeq = _clientSeq,
            FingerprintComponents = _fingerprint
        };

        try
        {
            var response = await _http.PostAsJsonAsync(
                new Uri($"v1/leases/{LeaseId}/renew", UriKind.Relative),
                renewDto,
                SymbolonProtocolJsonContext.Default.RenewRequestDto,
                ct).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadFromJsonAsync(
                    SymbolonProtocolJsonContext.Default.RenewResponseDto,
                    ct).ConfigureAwait(false);

                if (body is not null)
                {
                    Token = body.Token;
                    _expiresAt = body.ExpiresAt;
                    _clientSeq = body.LeaseSeq;
                    _graceStartedAt = null;

                    if (State != SeatState.Active)
                    {
                        State = SeatState.Active;
                    }
                }
                activity?.SetStatus(ActivityStatusCode.Ok);
            }
            else
            {
                activity?.SetStatus(ActivityStatusCode.Error, $"HTTP {(int)response.StatusCode}");
                HandleRenewalFailure($"Server returned {(int)response.StatusCode}");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            HandleRenewalFailure(ex.Message);
        }
    }

    private void HandleRenewalFailure(string reason)
    {
        var now = _time.GetUtcNow();

        if (State == SeatState.Active)
        {
            _graceStartedAt = now;
            State = SeatState.GracePeriod;
        }
        else if (State == SeatState.GracePeriod)
        {
            if (_graceStartedAt.HasValue && now - _graceStartedAt.Value > _gracePeriod)
            {
                State = SeatState.Lost;
            }
        }
    }

    /// <summary>
    /// Borrows the current active seat for offline roaming for the specified duration (1-30 days).
    /// Halts background heartbeats during the offline roaming period.
    /// </summary>
    public async Task<bool> BorrowAsync(int days, CancellationToken ct = default)
    {
        if (LeaseId is null || _http is null) return false;
        if (days < 1 || days > 30) throw new ArgumentOutOfRangeException(nameof(days), "Days must be between 1 and 30.");

        // Stop the heartbeat loop as offline devices do not send periodic heartbeats
        try
        {
            await _cts.CancelAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // already canceled
        }

        var dto = new BorrowRequestDto(days);
        var response = await _http.PostAsJsonAsync(
            new Uri($"v1/leases/{LeaseId}/borrow", UriKind.Relative),
            dto,
            SymbolonProtocolJsonContext.Default.BorrowRequestDto,
            ct).ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadFromJsonAsync(
                SymbolonProtocolJsonContext.Default.BorrowResponseDto,
                ct).ConfigureAwait(false);

            if (body is not null)
            {
                Token = body.Token;
                _expiresAt = body.BorrowedUntil;
                State = SeatState.Borrowed;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Explicitly returns a borrowed seat early back to the floating pool.
    /// </summary>
    public async Task<bool> ReturnBorrowedAsync(CancellationToken ct = default)
    {
        if (LeaseId is null || _http is null) return false;

        State = SeatState.Released;
        var response = await _http.DeleteAsync(
            new Uri($"v1/leases/{LeaseId}", UriKind.Relative),
            ct).ConfigureAwait(false);

        return response.IsSuccessStatusCode;
    }

    private bool _isDisposed;
    private readonly object _disposeLock = new();

    public async ValueTask DisposeAsync()
    {
        lock (_disposeLock)
        {
            if (_isDisposed) return;
            _isDisposed = true;
        }

        try
        {
            await _cts.CancelAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // already disposed
        }

        if (_heartbeatTask is not null)
        {
            try
            {
                await _heartbeatTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // normal cancellation
            }
        }

        // Do not release lease if the seat is borrowed for offline roaming
        if (LeaseId is not null && _state != SeatState.Released && _state != SeatState.Borrowed)
        {
            State = SeatState.Released;
            if (_http is not null)
            {
                try
                {
                    // Best-effort explicit release call
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    await _http.DeleteAsync(new Uri($"v1/leases/{LeaseId}", UriKind.Relative), cts.Token).ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is HttpRequestException or TimeoutException or OperationCanceledException)
                {
                    // Silent ignore on disposal failure
                }
            }
        }

        _cts.Dispose();
    }

    public void Dispose()
    {
        lock (_disposeLock)
        {
            if (_isDisposed) return;
            _isDisposed = true;
        }

        try
        {
            _cts.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // already disposed
        }

        if (_heartbeatTask is not null)
        {
            try
            {
                // Brief non-deadlocking wait for heartbeat loop exit
                _heartbeatTask.Wait(TimeSpan.FromMilliseconds(500));
            }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or AggregateException)
            {
                // ignore
            }
        }

        // Do not release lease if the seat is borrowed for offline roaming
        if (LeaseId is not null && _state != SeatState.Released && _state != SeatState.Borrowed)
        {
            State = SeatState.Released;
            if (_http is not null)
            {
                try
                {
                    // Synchronous send completely eliminates sync-over-async deadlock risk in UI threads
                    using var request = new HttpRequestMessage(HttpMethod.Delete, new Uri($"v1/leases/{LeaseId}", UriKind.Relative));
                    using var response = _http.Send(request);
                }
                catch (Exception ex) when (ex is HttpRequestException or TimeoutException or OperationCanceledException or InvalidOperationException)
                {
                    // Silent ignore on disposal failure
                }
            }
        }

        _cts.Dispose();
    }
}
