using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Symbolon.Format;
using Symbolon.Protocol;

namespace Symbolon.Client;

/// <summary>
/// Symbolon Client SDK for ISV applications integrating on-prem floating licenses.
/// Conforms to docs/08-referencna-implementacia.md §8.6.
/// </summary>
public sealed class SymbolonClient : IDisposable
{
    private readonly SymbolonClientOptions _options;
    private readonly HttpClient _http;
    private readonly bool _ownsHttpClient;
    private readonly LeaseTokenVerifier? _verifier;
    private readonly IReadOnlyDictionary<string, string> _fingerprint;
    private readonly ILogger? _log;

    public SymbolonClient(SymbolonClientOptions options, ILogger? log = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.ServerUri);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.LicenseKey);

        // Pre-validate license key structure and CRC-32C locally before network call (KEY-8)
        if (!LicenseKey.TryParse(options.LicenseKey, out _, out string? keyError))
        {
            throw new ArgumentException($"Preklep v licenčnom kľúči: {keyError}", nameof(options));
        }

        _options = options;
        _log = log;

        if (options.HttpClient is not null)
        {
            _http = options.HttpClient;
            _ownsHttpClient = false;
        }
        else
        {
            _http = new HttpClient { BaseAddress = options.ServerUri };
            _ownsHttpClient = true;
        }

        if (options.TrustedKeys is not null)
        {
            _verifier = new LeaseTokenVerifier(options.TrustedKeys, options.TimeProvider);
        }

        _fingerprint = options.CustomFingerprint ?? DeviceFingerprint.Collect();
    }

    /// <summary>
    /// Requests a floating seat allocation from the license server.
    /// </summary>
    public async Task<SeatLease> AcquireSeatAsync(
        IReadOnlyList<string>? features = null,
        CancellationToken ct = default)
    {
        var checkoutDto = new CheckoutRequestDto
        {
            LicenseKey = _options.LicenseKey,
            FingerprintComponents = _fingerprint,
            Quantity = 1,
            Features = features
        };

        HttpResponseMessage response;
        try
        {
            response = await _http.PostAsJsonAsync(
                new Uri("v1/leases", UriKind.Relative),
                checkoutDto,
                SymbolonProtocolJsonContext.Default.CheckoutRequestDto,
                ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is HttpRequestException or TimeoutException)
        {
            return SeatLease.Denied($"Offline: {ex.Message}");
        }

        if (!response.IsSuccessStatusCode)
        {
            return SeatLease.Denied($"Denied: Server returned {(int)response.StatusCode}");
        }

        var body = await response.Content.ReadFromJsonAsync(
            SymbolonProtocolJsonContext.Default.CheckoutResponseDto,
            ct).ConfigureAwait(false);

        if (body is null || string.IsNullOrWhiteSpace(body.LeaseId) || string.IsNullOrWhiteSpace(body.Token))
        {
            return SeatLease.Denied("Malformed server response");
        }

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
                return SeatLease.Denied($"Token verification failed: {verifyResult.FailureReason}");
            }
        }

        return new SeatLease(
            acquired: true,
            reason: null,
            leaseId: body.LeaseId,
            token: body.Token,
            seatNo: body.Seat,
            expiresAt: body.ExpiresAt,
            entitlements: body.Entitlements,
            http: _http,
            time: _options.TimeProvider,
            heartbeatInterval: _options.HeartbeatInterval,
            gracePeriod: _options.GracePeriod,
            fingerprint: _fingerprint,
            log: _log);
    }

    public void Dispose()
    {
        if (_ownsHttpClient)
        {
            _http.Dispose();
        }
    }
}
