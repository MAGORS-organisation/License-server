using System.Net.Http.Json;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using Symbolon.Protocol;

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

    public bool Acquired => _state is SeatState.Active or SeatState.GracePeriod;
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
            }
            else
            {
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

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync().ConfigureAwait(false);
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

        if (LeaseId is not null && _state != SeatState.Released)
        {
            State = SeatState.Released;
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

        _cts.Dispose();
    }

    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }
}
