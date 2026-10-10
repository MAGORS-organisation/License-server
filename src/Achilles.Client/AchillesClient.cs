using System.Diagnostics;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Achilles.Client.Discovery;
using Achilles.Client.Revocation;
using Achilles.Crypto;
using Achilles.Format;
using Achilles.Protocol;
using Achilles.Protocol.Tracing;

namespace Achilles.Client;

/// <summary>
/// Symbolon Client SDK for ISV applications integrating on-prem floating licenses.
/// Conforms to docs/08-referencna-implementacia.md §8.6.
/// Features resilient high-availability multi-server failover and zero-config discovery.
/// </summary>
public sealed class AchillesClient : IDisposable
{
    private readonly AchillesClientOptions _options;
    private readonly ServerFailoverPool _failoverPool;
    private readonly bool _ownsPool;
    private readonly LeaseTokenVerifier? _verifier;
    private readonly RevocationCache _revocationCache;
    private readonly IReadOnlyDictionary<string, string> _fingerprint;
    private readonly ILogger? _log;

    public ServerFailoverPool FailoverPool => _failoverPool;
    public RevocationCache RevocationCache => _revocationCache;

    public AchillesClient(AchillesClientOptions options, ILogger? log = null)
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

        _revocationCache = options.RevocationCache ?? new RevocationCache(options.TrustedKeys);
        _fingerprint = options.CustomFingerprint ?? DeviceFingerprint.Collect();
    }

    /// <summary>
    /// Requests a floating seat allocation from the license server with automatic multi-server failover.
    /// </summary>
    public Task<SeatLease> AcquireSeatAsync(
        IReadOnlyList<string>? features,
        CancellationToken ct) => AcquireSeatAsync(features, allowQueue: null, maxQueueWait: null, ct);

    /// <summary>
    /// Requests a floating seat allocation from the license server with automatic multi-server failover and optional queue waiting.
    /// </summary>
    public async Task<SeatLease> AcquireSeatAsync(
        IReadOnlyList<string>? features = null,
        bool? allowQueue = null,
        TimeSpan? maxQueueWait = null,
        CancellationToken ct = default)
    {
        using var activity = AchillesTracing.ActivitySource.StartActivity(AchillesTracing.OpCheckout);
        activity?.SetTag(AchillesTracing.TagLicenseId, _options.LicenseKey);

        // Pre-flight revocation check (RVL-12, RVL-13)
        if (_revocationCache.IsLicenseRevoked(_options.LicenseKey, out var revokedLicense))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "License revoked");
            throw new SymbolonRevocationException(revokedLicense);
        }

        string machineFpHash = FingerprintHelper.ComputeHash(_fingerprint);
        if (_revocationCache.IsMachineRevoked(machineFpHash, out var revokedMachine))
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Machine revoked");
            throw new SymbolonRevocationException(revokedMachine);
        }

        bool effectiveAllowQueue = allowQueue ?? _options.AllowQueue;
        var checkoutDto = new CheckoutRequestDto
        {
            LicenseKey = _options.LicenseKey,
            FingerprintComponents = _fingerprint,
            Quantity = 1,
            Features = features,
            AllowQueue = effectiveAllowQueue ? true : null,
            MachineId = _options.MachineId,
            UserId = _options.UserId,
            Priority = _options.Priority != 0 ? _options.Priority : null
        };

        try
        {
            return await _failoverPool.ExecuteWithFailoverAsync(async (serverUri, http, token) =>
            {
                var targetUri = new Uri(serverUri, "v1/leases");
                var response = await http.PostAsJsonAsync(
                    targetUri,
                    checkoutDto,
                    AchillesProtocolJsonContext.Default.CheckoutRequestDto,
                    token).ConfigureAwait(false);

                if ((int)response.StatusCode is 502 or 503 or 504)
                {
                    throw new HttpRequestException($"Server node {serverUri} returned {(int)response.StatusCode}");
                }

                if (response.StatusCode == System.Net.HttpStatusCode.Accepted)
                {
                    var queuedBody = await response.Content.ReadFromJsonAsync(
                        AchillesProtocolJsonContext.Default.QueuedResponseDto,
                        token).ConfigureAwait(false);

                    if (queuedBody is null || string.IsNullOrWhiteSpace(queuedBody.Ticket))
                    {
                        activity?.SetStatus(ActivityStatusCode.Error, "Malformed queued response");
                        return SeatLease.Denied("Malformed queued response");
                    }

                    if (!effectiveAllowQueue)
                    {
                        activity?.SetStatus(ActivityStatusCode.Error, "Queuing disabled on client");
                        return SeatLease.Denied($"Queue ticket {queuedBody.Ticket} issued but client auto-wait is disabled.");
                    }

                    TimeSpan waitTimeout = maxQueueWait ?? _options.MaxQueueWait;
                    using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(token);
                    waitCts.CancelAfter(waitTimeout);

                    int delaySec = (queuedBody.RetryAfterSeconds.HasValue && queuedBody.RetryAfterSeconds.Value > 0) ? queuedBody.RetryAfterSeconds.Value : 3;
                    if (response.Headers.RetryAfter?.Delta is { } delta)
                    {
                        delaySec = (int)delta.TotalSeconds;
                    }

                    string ticket = queuedBody.Ticket;
                    activity?.SetTag("queue.ticket", ticket);

                    try
                    {
                        while (!waitCts.IsCancellationRequested)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(delaySec, 1, 10)), _options.TimeProvider, waitCts.Token).ConfigureAwait(false);

                            var queueStatusUri = new Uri(serverUri, $"v1/queue/{Uri.EscapeDataString(ticket)}");
                            using var statusResp = await http.GetAsync(queueStatusUri, waitCts.Token).ConfigureAwait(false);

                            if (!statusResp.IsSuccessStatusCode)
                            {
                                activity?.SetStatus(ActivityStatusCode.Error, $"Queue status HTTP {(int)statusResp.StatusCode}");
                                return SeatLease.Denied($"Queue status request failed: {(int)statusResp.StatusCode}");
                            }

                            var statusDto = await statusResp.Content.ReadFromJsonAsync(
                                AchillesProtocolJsonContext.Default.QueueStatusResponseDto,
                                waitCts.Token).ConfigureAwait(false);

                            if (statusDto is null)
                            {
                                continue;
                            }

                            if (statusResp.Headers.RetryAfter?.Delta is { } pollDelta)
                            {
                                delaySec = (int)pollDelta.TotalSeconds;
                            }
                            else if (statusDto.RetryAfterSeconds.HasValue && statusDto.RetryAfterSeconds.Value > 0)
                            {
                                delaySec = statusDto.RetryAfterSeconds.Value;
                            }

                            if (statusDto.Status == "ready" && !string.IsNullOrWhiteSpace(statusDto.LeaseId) && !string.IsNullOrWhiteSpace(statusDto.Token))
                            {
                                activity?.SetTag(AchillesTracing.TagLeaseId, statusDto.LeaseId);

                                if (_revocationCache.IsLeaseRevoked(statusDto.LeaseId, out var revokedQueueLease))
                                {
                                    activity?.SetStatus(ActivityStatusCode.Error, "Lease revoked");
                                    throw new SymbolonRevocationException(revokedQueueLease);
                                }

                                if (_verifier is not null)
                                {
                                    string expectedFpHash = FingerprintHelper.ComputeHash(_fingerprint);
                                    var verifyResult = _verifier.Verify(
                                        token: statusDto.Token,
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
                                    leaseId: statusDto.LeaseId,
                                    token: statusDto.Token,
                                    seatNo: statusDto.Seat ?? 1,
                                    expiresAt: statusDto.ExpiresAt ?? _options.TimeProvider.GetUtcNow().AddMinutes(10),
                                    entitlements: features ?? Array.Empty<string>(),
                                    http: http,
                                    time: _options.TimeProvider,
                                    heartbeatInterval: _options.HeartbeatInterval,
                                    gracePeriod: _options.GracePeriod,
                                    fingerprint: _fingerprint,
                                    log: _log);
                            }

                            if (statusDto.Status is "cancelled" or "expired")
                            {
                                activity?.SetStatus(ActivityStatusCode.Error, $"Queue ticket {statusDto.Status}");
                                return SeatLease.Denied($"Queue ticket {statusDto.Status}");
                            }
                        }
                    }
                    catch (OperationCanceledException) when (!token.IsCancellationRequested)
                    {
                        try
                        {
                            var cancelUri = new Uri(serverUri, $"v1/queue/{Uri.EscapeDataString(ticket)}");
                            using var cancelResp = await http.DeleteAsync(cancelUri, CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (HttpRequestException)
                        {
                            // best effort cleanup
                        }

                        activity?.SetStatus(ActivityStatusCode.Error, "Queue timeout exceeded");
                        return SeatLease.Denied("Queue wait timeout exceeded");
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested)
                    {
                        try
                        {
                            var cancelUri = new Uri(serverUri, $"v1/queue/{Uri.EscapeDataString(ticket)}");
                            using var cancelResp = await http.DeleteAsync(cancelUri, CancellationToken.None).ConfigureAwait(false);
                        }
                        catch (HttpRequestException)
                        {
                            // best effort cleanup
                        }
                        throw;
                    }
                }

                if (!response.IsSuccessStatusCode)
                {
                    activity?.SetStatus(ActivityStatusCode.Error, $"HTTP {(int)response.StatusCode}");
                    return SeatLease.Denied($"Denied: Server returned {(int)response.StatusCode}");
                }

                var body = await response.Content.ReadFromJsonAsync(
                    AchillesProtocolJsonContext.Default.CheckoutResponseDto,
                    token).ConfigureAwait(false);

                if (body is null || string.IsNullOrWhiteSpace(body.LeaseId) || string.IsNullOrWhiteSpace(body.Token))
                {
                    activity?.SetStatus(ActivityStatusCode.Error, "Malformed server response");
                    return SeatLease.Denied("Malformed server response");
                }

                activity?.SetTag(AchillesTracing.TagLeaseId, body.LeaseId);

                if (_revocationCache.IsLeaseRevoked(body.LeaseId, out var revokedLease))
                {
                    activity?.SetStatus(ActivityStatusCode.Error, "Lease revoked");
                    throw new SymbolonRevocationException(revokedLease);
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
    public async Task<BorrowResponseDto?> BorrowSeatAsync(
        string leaseId,
        int days,
        string? possessionPublicKeyJwk = null,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseId);
        if (days < 1 || days > 30)
        {
            throw new ArgumentOutOfRangeException(nameof(days), "Days must be between 1 and 30.");
        }

        using var activity = AchillesTracing.ActivitySource.StartActivity(AchillesTracing.OpBorrow);
        activity?.SetTag(AchillesTracing.TagLeaseId, leaseId);
        activity?.SetTag(AchillesTracing.TagBorrowDays, days);

        var dto = new BorrowRequestDto(days, possessionPublicKeyJwk);

        try
        {
            return await _failoverPool.ExecuteWithFailoverAsync(async (serverUri, http, token) =>
            {
                var targetUri = new Uri(serverUri, $"v1/leases/{leaseId}/borrow");
                var response = await http.PostAsJsonAsync(
                    targetUri,
                    dto,
                    AchillesProtocolJsonContext.Default.BorrowRequestDto,
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
                    AchillesProtocolJsonContext.Default.BorrowResponseDto,
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
    /// Obtains a single-use return challenge nonce for early return of an offline roaming borrowed seat (FLT-21).
    /// </summary>
    public async Task<ReturnChallengeResponseDto?> GetReturnChallengeAsync(string leaseId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseId);

        try
        {
            return await _failoverPool.ExecuteWithFailoverAsync(async (serverUri, http, token) =>
            {
                var targetUri = new Uri(serverUri, $"v1/leases/{leaseId}/return-challenge");
                var response = await http.PostAsync(targetUri, null, token).ConfigureAwait(false);

                if ((int)response.StatusCode is 502 or 503 or 504)
                {
                    throw new HttpRequestException($"Server node {serverUri} returned {(int)response.StatusCode}");
                }

                if (!response.IsSuccessStatusCode)
                {
                    return null;
                }

                return await response.Content.ReadFromJsonAsync(
                    AchillesProtocolJsonContext.Default.ReturnChallengeResponseDto,
                    token).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
        }
        catch (SymbolonFailoverExhaustedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns a previously borrowed seat back to the floating pool early using cryptographic proof-of-possession (FLT-21, FLT-22).
    /// </summary>
    public async Task<bool> ReturnBorrowedSeatAsync(
        string leaseId,
        string symleasePem,
        string possessionPrivateKeyJwk,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(symleasePem);
        ArgumentException.ThrowIfNullOrWhiteSpace(possessionPrivateKeyJwk);

        using var activity = AchillesTracing.ActivitySource.StartActivity(AchillesTracing.OpReturnBorrowed);
        activity?.SetTag(AchillesTracing.TagLeaseId, leaseId);

        var challenge = await GetReturnChallengeAsync(leaseId, ct).ConfigureAwait(false);
        if (challenge is null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, "Failed to obtain return challenge");
            return false;
        }

        string signature = ProofOfPossessionEngine.SignChallenge(challenge.Nonce, possessionPrivateKeyJwk);

        var requestDto = new EarlyReturnRequestDto
        {
            Symlease = symleasePem,
            Nonce = challenge.Nonce,
            Signature = signature
        };

        try
        {
            return await _failoverPool.ExecuteWithFailoverAsync(async (serverUri, http, token) =>
            {
                var targetUri = new Uri(serverUri, $"v1/leases/{leaseId}/return");
                var response = await http.PostAsJsonAsync(
                    targetUri,
                    requestDto,
                    AchillesProtocolJsonContext.Default.EarlyReturnRequestDto,
                    token).ConfigureAwait(false);

                if ((int)response.StatusCode is 502 or 503 or 504)
                {
                    throw new HttpRequestException($"Server node {serverUri} returned {(int)response.StatusCode}");
                }

                if (response.IsSuccessStatusCode)
                {
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    return true;
                }

                activity?.SetStatus(ActivityStatusCode.Error, $"HTTP {(int)response.StatusCode}");
                return false;
            }, ct).ConfigureAwait(false);
        }
        catch (SymbolonFailoverExhaustedException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Attempts to return a borrowed seat without cryptographic proof-of-possession (FLT-22).
    /// Returns false if rejected by the server with 403 Forbidden.
    /// </summary>
    [Obsolete("Use ReturnBorrowedSeatAsync with symlease and possessionKey to provide cryptographic proof-of-possession (FLT-21).")]
    public async Task<bool> ReturnBorrowedSeatAsync(string leaseId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseId);

        using var activity = AchillesTracing.ActivitySource.StartActivity(AchillesTracing.OpReturnBorrowed);
        activity?.SetTag(AchillesTracing.TagLeaseId, leaseId);

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

    /// <summary>
    /// Synchronizes the client's revocation cache with the cluster using the failover pool.
    /// </summary>
    public async Task<bool> SyncRevocationsAsync(RevocationListVerifier verifier, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(verifier);
        try
        {
            return await _failoverPool.ExecuteWithFailoverAsync(async (serverUri, http, token) =>
            {
                return await _revocationCache.SyncAsync(http, serverUri, verifier, token).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
        }
        catch (SymbolonFailoverExhaustedException)
        {
            return false;
        }
    }

    /// <summary>
    /// Activates a node-locked machine for the license using the failover pool (FPR-15).
    /// </summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA1848:Use the LoggerMessage delegates", Justification = "SDK client warning logging")]
    public async Task<ActivationResponseDto> ActivateMachineAsync(
        IReadOnlyDictionary<string, string>? customComponents = null,
        string? machineId = null,
        CancellationToken ct = default)
    {
        using var activity = AchillesTracing.ActivitySource.StartActivity("ActivateMachine");
        activity?.SetTag("licenseKey", _options.LicenseKey);

        var components = customComponents is not null
            ? FingerprintHelper.FilterValidComponents(customComponents)
            : DeviceFingerprint.Collect(_options.LicenseKey, warningLogger: msg => _log?.LogWarning("{Warning}", msg)).ToDictionary(kv => kv.Key, kv => kv.Value);

        var requestDto = new ActivationRequestDto
        {
            LicenseKey = _options.LicenseKey,
            FingerprintComponents = components,
            MachineId = machineId ?? _options.MachineId ?? Environment.MachineName
        };

        return await _failoverPool.ExecuteWithFailoverAsync(async (serverUri, http, token) =>
        {
            var targetUri = new Uri(serverUri, "/v1/activations");
            using var reqMsg = new HttpRequestMessage(HttpMethod.Post, targetUri)
            {
                Content = JsonContent.Create(requestDto, AchillesProtocolJsonContext.Default.ActivationRequestDto)
            };

            var response = await http.SendAsync(reqMsg, token).ConfigureAwait(false);

            if ((int)response.StatusCode is 502 or 503 or 504)
            {
                throw new HttpRequestException($"Server node {serverUri} returned {(int)response.StatusCode}");
            }

            if (!response.IsSuccessStatusCode)
            {
                var errorDetail = await response.Content.ReadAsStringAsync(token).ConfigureAwait(false);
                activity?.SetStatus(ActivityStatusCode.Error, $"Activation failed: {response.StatusCode} {errorDetail}");
                throw new HttpRequestException($"Activation failed with status code {response.StatusCode}: {errorDetail}");
            }

            var activation = await response.Content.ReadFromJsonAsync(
                AchillesProtocolJsonContext.Default.ActivationResponseDto,
                token).ConfigureAwait(false);

            activity?.SetStatus(ActivityStatusCode.Ok);
            return activation ?? throw new InvalidOperationException("Empty activation response from server.");
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Deactivates a node-locked machine by its activation ID (FPR-15).
    /// </summary>
    public async Task<bool> DeactivateMachineAsync(string activationId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(activationId);

        using var activity = AchillesTracing.ActivitySource.StartActivity("DeactivateMachine");
        activity?.SetTag("activationId", activationId);

        try
        {
            return await _failoverPool.ExecuteWithFailoverAsync(async (serverUri, http, token) =>
            {
                var targetUri = new Uri(serverUri, $"/v1/activations/{Uri.EscapeDataString(activationId)}");
                var response = await http.DeleteAsync(targetUri, token).ConfigureAwait(false);

                if ((int)response.StatusCode is 502 or 503 or 504)
                {
                    throw new HttpRequestException($"Server node {serverUri} returned {(int)response.StatusCode}");
                }

                if (response.IsSuccessStatusCode)
                {
                    activity?.SetStatus(ActivityStatusCode.Ok);
                    return true;
                }

                activity?.SetStatus(ActivityStatusCode.Error, $"HTTP {(int)response.StatusCode}");
                return false;
            }, ct).ConfigureAwait(false);
        }
        catch (SymbolonFailoverExhaustedException ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            return false;
        }
    }

    /// <summary>
    /// Reserves credits for a metered feature operation from a token wallet.
    /// </summary>
    public async Task<ReserveTokensResponseDto> ReserveTokensAsync(ReserveTokensRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var activity = AchillesTracing.ActivitySource.StartActivity("ReserveTokens");
        activity?.SetTag("walletId", request.WalletId);
        activity?.SetTag("featureCode", request.FeatureCode);

        return await _failoverPool.ExecuteWithFailoverAsync(async (serverUri, http, token) =>
        {
            var targetUri = new Uri(serverUri, "v1/tokens/reserve");
            var response = await http.PostAsJsonAsync(
                targetUri,
                request,
                AchillesProtocolJsonContext.Default.ReserveTokensRequestDto,
                token).ConfigureAwait(false);

            if ((int)response.StatusCode is 502 or 503 or 504)
            {
                throw new HttpRequestException($"Server node {serverUri} returned {(int)response.StatusCode}");
            }

            var result = await response.Content.ReadFromJsonAsync(
                AchillesProtocolJsonContext.Default.ReserveTokensResponseDto,
                token).ConfigureAwait(false);

            return result ?? new ReserveTokensResponseDto
            {
                Success = false,
                FailureReason = $"Failed to parse reserve tokens response (HTTP {(int)response.StatusCode})."
            };
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends a heartbeat to extend or incrementally consume reserved credits during a long-running metered task.
    /// </summary>
    public async Task<HeartbeatTokensResponseDto> HeartbeatTokensAsync(HeartbeatTokensRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var activity = AchillesTracing.ActivitySource.StartActivity("HeartbeatTokens");
        activity?.SetTag("reservationId", request.ReservationId);

        return await _failoverPool.ExecuteWithFailoverAsync(async (serverUri, http, token) =>
        {
            var targetUri = new Uri(serverUri, "v1/tokens/heartbeat");
            var response = await http.PostAsJsonAsync(
                targetUri,
                request,
                AchillesProtocolJsonContext.Default.HeartbeatTokensRequestDto,
                token).ConfigureAwait(false);

            if ((int)response.StatusCode is 502 or 503 or 504)
            {
                throw new HttpRequestException($"Server node {serverUri} returned {(int)response.StatusCode}");
            }

            var result = await response.Content.ReadFromJsonAsync(
                AchillesProtocolJsonContext.Default.HeartbeatTokensResponseDto,
                token).ConfigureAwait(false);

            return result ?? new HeartbeatTokensResponseDto
            {
                Success = false,
                FailureReason = $"Failed to parse heartbeat tokens response (HTTP {(int)response.StatusCode})."
            };
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Commits a token reservation with the actual units consumed, refunding unused credits.
    /// </summary>
    public async Task<CommitTokensResponseDto> CommitTokensAsync(CommitTokensRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var activity = AchillesTracing.ActivitySource.StartActivity("CommitTokens");
        activity?.SetTag("reservationId", request.ReservationId);

        return await _failoverPool.ExecuteWithFailoverAsync(async (serverUri, http, token) =>
        {
            var targetUri = new Uri(serverUri, "v1/tokens/commit");
            var response = await http.PostAsJsonAsync(
                targetUri,
                request,
                AchillesProtocolJsonContext.Default.CommitTokensRequestDto,
                token).ConfigureAwait(false);

            if ((int)response.StatusCode is 502 or 503 or 504)
            {
                throw new HttpRequestException($"Server node {serverUri} returned {(int)response.StatusCode}");
            }

            var result = await response.Content.ReadFromJsonAsync(
                AchillesProtocolJsonContext.Default.CommitTokensResponseDto,
                token).ConfigureAwait(false);

            return result ?? new CommitTokensResponseDto
            {
                Success = false,
                FailureReason = $"Failed to parse commit tokens response (HTTP {(int)response.StatusCode})."
            };
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Rolls back an open token reservation, restoring all reserved credits.
    /// </summary>
    public async Task<RollbackTokensResponseDto> RollbackTokensAsync(RollbackTokensRequestDto request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var activity = AchillesTracing.ActivitySource.StartActivity("RollbackTokens");
        activity?.SetTag("reservationId", request.ReservationId);

        return await _failoverPool.ExecuteWithFailoverAsync(async (serverUri, http, token) =>
        {
            var targetUri = new Uri(serverUri, "v1/tokens/rollback");
            var response = await http.PostAsJsonAsync(
                targetUri,
                request,
                AchillesProtocolJsonContext.Default.RollbackTokensRequestDto,
                token).ConfigureAwait(false);

            if ((int)response.StatusCode is 502 or 503 or 504)
            {
                throw new HttpRequestException($"Server node {serverUri} returned {(int)response.StatusCode}");
            }

            var result = await response.Content.ReadFromJsonAsync(
                AchillesProtocolJsonContext.Default.RollbackTokensResponseDto,
                token).ConfigureAwait(false);

            return result ?? new RollbackTokensResponseDto
            {
                Success = false,
                FailureReason = $"Failed to parse rollback tokens response (HTTP {(int)response.StatusCode})."
            };
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Fetches the real-time balance and overdraft status of a token wallet.
    /// </summary>
    public async Task<TokenWalletBalanceResponseDto?> GetTokenWalletBalanceAsync(string walletId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(walletId);
        using var activity = AchillesTracing.ActivitySource.StartActivity("GetTokenWalletBalance");
        activity?.SetTag("walletId", walletId);

        return await _failoverPool.ExecuteWithFailoverAsync(async (serverUri, http, token) =>
        {
            var targetUri = new Uri(serverUri, $"v1/tokens/wallets/{Uri.EscapeDataString(walletId)}/balance");
            var response = await http.GetAsync(targetUri, token).ConfigureAwait(false);

            if ((int)response.StatusCode is 502 or 503 or 504)
            {
                throw new HttpRequestException($"Server node {serverUri} returned {(int)response.StatusCode}");
            }

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            return await response.Content.ReadFromJsonAsync(
                AchillesProtocolJsonContext.Default.TokenWalletBalanceResponseDto,
                token).ConfigureAwait(false);
        }, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Begins a metered pay-as-you-go operation wrapped in an auto-rollback scope.
    /// If not committed explicitly, disposing the scope will automatically roll back reserved credits.
    /// </summary>
    public async Task<TokenReservationScope> BeginMeteredScopeAsync(
        string walletId,
        string featureCode,
        decimal estimatedUnits,
        bool isDurationMinutes = false,
        TimeSpan? reservationTtl = null,
        string? clientRef = null,
        CancellationToken ct = default)
    {
        var request = new ReserveTokensRequestDto
        {
            WalletId = walletId,
            FeatureCode = featureCode,
            EstimatedUnits = estimatedUnits,
            IsDurationMinutes = isDurationMinutes,
            ReservationTtl = reservationTtl,
            ClientRef = clientRef,
            MachineId = _options.MachineId
        };

        var response = await ReserveTokensAsync(request, ct).ConfigureAwait(false);
        if (!response.Success || string.IsNullOrWhiteSpace(response.ReservationId))
        {
            throw new InvalidOperationException($"Token reservation failed for wallet '{walletId}': {response.FailureReason ?? "Insufficient credits or wallet inactive"}");
        }

        return new TokenReservationScope(
            this,
            walletId,
            response.ReservationId,
            featureCode,
            response.ReservedAmount,
            response.AvailableBalance);
    }

    public void Dispose()
    {
        if (_ownsPool)
        {
            _failoverPool.Dispose();
        }
    }
}
