using System.Security.Cryptography;
using System.Text;
using Symbolon.Crypto;
using Symbolon.Domain;
using Symbolon.Domain.Transparency;
using Symbolon.Format;

namespace Symbolon.Relay;

/// <summary>
/// Manages air-gapped seat grant operations on the relay side (GNT-1..10, FLT-32..35).
/// </summary>
internal sealed class RelaySeatGrantManager
{
    private readonly SqliteSeatStore _seatStore;
    private readonly IKeyRing _keyRing;
    private readonly ISignatureProvider _relayKey;
    private readonly IAuditLedger? _auditLedger;
    private readonly TimeProvider _timeProvider;

    public RelaySeatGrantManager(
        SqliteSeatStore seatStore,
        IKeyRing keyRing,
        ISignatureProvider relayKey,
        IAuditLedger? auditLedger = null,
        TimeProvider? timeProvider = null)
    {
        _seatStore = seatStore ?? throw new ArgumentNullException(nameof(seatStore));
        _keyRing = keyRing ?? throw new ArgumentNullException(nameof(keyRing));
        _relayKey = relayKey ?? throw new ArgumentNullException(nameof(relayKey));
        _auditLedger = auditLedger;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Imports and verifies a .symgrant PEM document, enforcing sequence monotonicity (GNT-7),
    /// supersedes cleanup (GNT-9), and non-overlapping seat ranges (GNT-10).
    /// </summary>
    public async Task<SeatGrantVerificationResult> ImportGrantAsync(string grantPem, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(grantPem);

        // Pre-parse to obtain license ID and sequence
        var probeVerifier = new SeatGrantVerifier(_keyRing, _timeProvider);
        var probeResult = probeVerifier.Verify(grantPem);
        if (!probeResult.IsValid || probeResult.Claims is null)
        {
            return probeResult;
        }

        string licenseId = probeResult.Claims.Sub;
        long lastSeq = await _seatStore.GetLastSeqAsync(licenseId, ct).ConfigureAwait(false);

        // Strict verification with MinSeq = lastSeq (GNT-7)
        var verifier = new SeatGrantVerifier(_keyRing, _timeProvider, options: new SeatGrantVerifierOptions
        {
            Strictness = Strictness.Strict,
            ExpectedRelayId = _relayKey.Kid,
            MinSeq = lastSeq
        });

        var result = verifier.Verify(grantPem);
        if (!result.IsValid || result.Claims is null)
        {
            return result;
        }

        // Store into SQLite
        await _seatStore.ImportSeatGrantAsync(result.Claims, grantPem, ct).ConfigureAwait(false);

        // If grant provides private lease key, register into keyring for local token verification/signing
        if (!string.IsNullOrWhiteSpace(result.Claims.Symgrant.LeaseKey.D))
        {
            try
            {
                var leaseProvider = Es256SignatureProvider.ImportJwk(result.Claims.Symgrant.LeaseKey);
                if (_keyRing is SymbolonKeyRing sk)
                {
                    sk.Add(leaseProvider);
                }
            }
            catch (ArgumentException)
            {
                // Incomplete or invalid JWK format
            }
        }

        return result;
    }

    /// <summary>
    /// Generates a signed .symreq air-gapped grant request artifact (FLT-32).
    /// </summary>
    public async Task<string> CreateGrantRequestAsync(
        string licenseKey,
        string licenseId,
        int requestedSeats,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(licenseKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(licenseId);
        if (requestedSeats <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedSeats), "Requested seats must be positive.");
        }

        long lastSeq = await _seatStore.GetLastSeqAsync(licenseId, ct).ConfigureAwait(false);
        string usageDigest = ComputeUsageDigest(licenseId);
        string nonce = $"nonce_{Guid.NewGuid():N}";
        long now = _timeProvider.GetUtcNow().ToUnixTimeSeconds();

        var claims = new AirGapRequestClaims
        {
            Iss = _relayKey.Kid,
            Sub = licenseKey,
            Jti = $"req_{Guid.NewGuid():N}",
            Iat = now,
            Exp = now + 7200, // 2h request validity
            Symreq = new AirGapRequestPayload
            {
                V = 1,
                RelayId = _relayKey.Kid,
                LicenseKey = licenseKey,
                RequestedSeats = requestedSeats,
                LastSeq = lastSeq,
                UsageDigest = usageDigest,
                Nonce = nonce
            }
        };

        var signer = new AirGapRequestSigner(_relayKey);
        return signer.Sign(claims);
    }

    private string ComputeUsageDigest(string licenseId)
    {
        if (_auditLedger is InMemoryAuditLedger inMem)
        {
            var licenseEvents = inMem.Events
                .Where(e => string.Equals(e.LicenseId, licenseId, StringComparison.Ordinal))
                .ToList();

            if (licenseEvents.Count > 0)
            {
                var leafHashes = licenseEvents.Select(e =>
                {
                    byte[] data = Encoding.UTF8.GetBytes($"{e.Type}:{e.LicenseId}:{e.LeaseId}:{e.Timestamp:O}");
                    return MerkleTree.HashLeaf(data);
                }).ToList();

                byte[] root = MerkleTree.ComputeRootHash(leafHashes);
                return $"sha256:{Convert.ToHexStringLower(root)}";
            }
        }

        // Default empty chain digest
        byte[] emptyHash = SHA256.HashData(Encoding.UTF8.GetBytes($"empty-chain:{licenseId}"));
        return $"sha256:{Convert.ToHexStringLower(emptyHash)}";
    }
}
