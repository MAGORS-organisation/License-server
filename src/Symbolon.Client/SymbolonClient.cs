using System.Diagnostics;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Symbolon.Client.Discovery;
using Symbolon.Format;
using Symbolon.Protocol;
using Symbolon.Protocol.Tracing;

namespace Symbolon.Client;

/// <summary>
/// Symbolon Client SDK for ISV applications integrating on-prem floating licenses.
/// Conforms to docs/08-referencna-implementacia.md §8.6.
/// Features resilient high-availability multi-server failover and zero-config discovery.
/// </summary>
public sealed class SymbolonClient : IDisposable
{
    private readonly SymbolonClientOptions _options;
    private readonly ServerFailoverPool _failoverPool;
    private readonly bool _ownsPool;
    private readonly LeaseTokenVerifier? _verifier;
    private readonly IReadOnlyDictionary<string, string> _fingerprint;
    private readonly ILogger? _log;

    public ServerFailoverPool FailoverPool => _failoverPool;

    public SymbolonClient(SymbolonClientOptions options, ILogger? log = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.LicenseKey);

        // Pre-validate license key structure and CRC-32C locally before network call (KEY-8)
        if (!LicenseKey.TryParse(options.LicenseKey, out _, out string? keyError))
        {
            throw new ArgumentException($"Preklep v licenčnom kľúči: {keyError}", nameof(options));
        }

        _options = options;
        _log = log;

        if (options.FailoverPool is not null)
        {
            _failoverPool = options.FailoverPool;
            _ownsPool = false;
        }
        else
        {
            var servers = new List<Uri>();

            if (options.ServerUri is not null)
            {
                servers.Add(options.ServerUri);
            }

            if (options.ServerUris is not null)
            {
                foreach (var uri in options.ServerUris)
                {
                    if (!servers.Contains(uri)) servers.Add(uri);
                }
            }

            if (servers.Count == 0 && options.AutoDiscover)
            {
                var resolved = SymbolonServerResolver.Resolve(fallbackToEnvironment: true);
                foreach (var uri in resolved)
                {
                    if (!servers.Contains(uri)) servers.Add(uri);
                }
            }

            if (servers.Count == 0 && options.ServerUri is null)
            {
                var envResolved = SymbolonServerResolver.Resolve(fallbackToEnvironment: true);
                if (envResolved.Count > 0)
                {
                    servers.AddRange(envResolved);
                }
                else
                {
                    servers.Add(new Uri("http://localhost:8080"));
                }
            }

            _failoverPool = new ServerFailoverPool(
                servers,
                customHttpClient: options.HttpClient,
                timeProvider: options.TimeProvider,
                logger: log);
            _ownsPool = true;
        }

        if (options.TrustedKeys is not null)
        {
            _verifier = new LeaseTokenVerifier(options.TrustedKeys, options.TimeProvider);
        }

        _fingerprint = options.CustomFingerprint ?? DeviceFingerprint.Collect();
    }

    /// <summary>
    /// Requests a floating seat allocation from the license server with automatic multi-server failover.
    /// </summary>
    public async Task<SeatLease> AcquireSeatAsync(
        IReadOnlyList<string>? features = null,
        CancellationToken ct = default)
    {
        using var activity = SymbolonTracing.ActivitySource.StartActivity(SymbolonTracing.OpCheckout);
        activity?.SetTag(SymbolonTracing.TagLicenseId, _options.LicenseKey);

        var checkoutDto = new CheckoutRequestDto
        {
            LicenseKey = _options.LicenseKey,
            FingerprintComponents = _fingerprint,
            Quantity = 1,
            Features = features
        };

        try
        {
            return await _failoverPool.ExecuteWithFailoverAsync(async (serverUri, http, token) =>
            {
                var targetUri = new Uri(serverUri, "v1/leases");
                var response = await http.PostAsJsonAsync(
                    targetUri,
                    checkoutDto,
                    SymbolonProtocolJsonContext.Default.CheckoutRequestDto,
                    token).ConfigureAwait(false);

                if ((int)response.StatusCode is 502 or 503 or 504)
                {
                    throw new HttpRequestException($"Server node {serverUri} returned {(int)response.StatusCode}");
                }

                if (!response.IsSuccessStatusCode)
                {
                    activity?.SetStatus(ActivityStatusCode.Error, $"HTTP {(int)response.StatusCode}");
                    return SeatLease.Denied($"Denied: Server returned {(int)response.StatusCode}");
                }

                var body = await response.Content.ReadFromJsonAsync(
                    SymbolonProtocolJsonContext.Default.CheckoutResponseDto,
                    token).ConfigureAwait(false);

                if (body is null || string.IsNullOrWhiteSpace(body.LeaseId) || string.IsNullOrWhiteSpace(body.Token))
                {
                    activity?.SetStatus(ActivityStatusCode.Error, "Malformed server response");
                    return SeatLease.Denied("Malformed server response");
                }

                activity?.SetTag(SymbolonTracing.TagLeaseId, body.LeaseId);

                // Verify the token if trusted keys are configured
                if (_verifier is not null)
                {
                    string expectedFpHash = FingerprintHelper.ComputeHash(_fingerprint);
                    var verifyResult = _verifier.Verify(
                        token: body.Token,
                        expectedFpHash: expectedFpHash,
                        expectedLicenseId: null);

                    if (!verifyResult.IsValid)
                    {
                        activity?.SetStatus(ActivityStatusCode.Error, "Token verification failed");
                        return SeatLease.Denied($"Token verification failed: {verifyResult.FailureReason}");
                    }
                }

                activity?.SetStatus(ActivityStatusCode.Ok);
                return new SeatLease(
                    acquired: true,
                    reason: null,
                    leaseId: body.LeaseId,
                    token: body.Token,
                    seatNo: body.Seat,
                    expiresAt: body.ExpiresAt,
                    entitlements: body.Entitlements,
                    http: http,
                    time: _options.TimeProvider,
                    heartbeatInterval: _options.HeartbeatInterval,
                    gracePeriod: _options.GracePeriod,
                    fingerprint: _fingerprint,
                    log: _log);
            }, ct).ConfigureAwait(false);
        }
        catch (SymbolonFailoverExhaustedException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return SeatLease.Denied($"Offline: {ex.Message}");
        }
    }

    /// <summary>
    /// Borrows an active floating seat for offline roaming for up to 30 days.
    /// </summary>
    public async Task<BorrowResponseDto?> BorrowSeatAsync(string leaseId, int days, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseId);
        if (days < 1 || days > 30)
        {
            throw new ArgumentOutOfRangeException(nameof(days), "Days must be between 1 and 30.");
        }

        using var activity = SymbolonTracing.ActivitySource.StartActivity(SymbolonTracing.OpBorrow);
        activity?.SetTag(SymbolonTracing.TagLeaseId, leaseId);
        activity?.SetTag(SymbolonTracing.TagBorrowDays, days);

        var dto = new BorrowRequestDto(days);

        try
        {
            return await _failoverPool.ExecuteWithFailoverAsync(async (serverUri, http, token) =>
            {
                var targetUri = new Uri(serverUri, $"v1/leases/{leaseId}/borrow");
                var response = await http.PostAsJsonAsync(
                    targetUri,
                    dto,
                    SymbolonProtocolJsonContext.Default.BorrowRequestDto,
                    token).ConfigureAwait(false);

                if ((int)response.StatusCode is 502 or 503 or 504)
                {
                    throw new HttpRequestException($"Server node {serverUri} returned {(int)response.StatusCode}");
                }

                if (!response.IsSuccessStatusCode)
                {
                    activity?.SetStatus(ActivityStatusCode.Error, $"HTTP {(int)response.StatusCode}");
                    return null;
                }

                var result = await response.Content.ReadFromJsonAsync(
                    SymbolonProtocolJsonContext.Default.BorrowResponseDto,
                    token).ConfigureAwait(false);

                activity?.SetStatus(ActivityStatusCode.Ok);
                return result;
            }, ct).ConfigureAwait(false);
        }
        catch (SymbolonFailoverExhaustedException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Returns a previously borrowed seat back to the floating pool early.
    /// </summary>
    public async Task<bool> ReturnBorrowedSeatAsync(string leaseId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseId);

        using var activity = SymbolonTracing.ActivitySource.StartActivity(SymbolonTracing.OpReturnBorrowed);
        activity?.SetTag(SymbolonTracing.TagLeaseId, leaseId);

        try
        {
            return await _failoverPool.ExecuteWithFailoverAsync(async (serverUri, http, token) =>
            {
                var targetUri = new Uri(serverUri, $"v1/leases/{leaseId}");
                var response = await http.DeleteAsync(
                    targetUri,
                    token).ConfigureAwait(false);

                if ((int)response.StatusCode is 502 or 503 or 504)
                {
                    throw new HttpRequestException($"Server node {serverUri} returned {(int)response.StatusCode}");
                }

                if (response.IsSuccessStatusCode)
                {
                    activity?.SetStatus(ActivityStatusCode.Ok);
                }
                else
                {
                    activity?.SetStatus(ActivityStatusCode.Error, $"HTTP {(int)response.StatusCode}");
                }

                return response.IsSuccessStatusCode;
            }, ct).ConfigureAwait(false);
        }
        catch (SymbolonFailoverExhaustedException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return false;
        }
    }

    public void Dispose()
    {
        if (_ownsPool)
        {
            _failoverPool.Dispose();
        }
    }
}
