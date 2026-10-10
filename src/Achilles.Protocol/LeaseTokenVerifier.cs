using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using Achilles.Crypto;

namespace Achilles.Protocol;

public sealed record LeaseVerificationResult(
    bool IsValid,
    LeaseClaims? Claims,
    string? FailureReason)
{
    public static LeaseVerificationResult Success(LeaseClaims claims) => new(true, claims, null);
    public static LeaseVerificationResult Fail(string reason) => new(false, null, reason);
}

/// <summary>
/// Verifies compact JWS Lease Tokens according to spec/04-lease-token.md (LSE-11 to LSE-14).
/// </summary>
public sealed class LeaseTokenVerifier
{
    private readonly IKeyRing _keyRing;
    private readonly TimeProvider _timeProvider;

    public LeaseTokenVerifier(IKeyRing keyRing, TimeProvider? timeProvider = null)
    {
        _keyRing = keyRing ?? throw new ArgumentNullException(nameof(keyRing));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Verifies signature, expiration, holder fingerprint, and monotonicity of sequence number.
    /// </summary>
    public LeaseVerificationResult Verify(
        string token,
        string? expectedFpHash = null,
        string? expectedLicenseId = null,
        long lastSeenSeq = 0,
        TimeSpan? clockSkew = null)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return LeaseVerificationResult.Fail("empty-token");
        }

        string[] parts = token.Split('.');
        if (parts.Length != 3)
        {
            return LeaseVerificationResult.Fail("malformed-compact-jws"); // LSE-1
        }

        // 1. Parse header
        LeaseProtectedHeader? header;
        try
        {
            byte[] headerBytes = Base64Url.DecodeFromChars(parts[0]);
            header = JsonSerializer.Deserialize(headerBytes, AchillesProtocolJsonContext.Default.LeaseProtectedHeader);
        }
        catch (FormatException)
        {
            return LeaseVerificationResult.Fail("malformed-header");
        }
        catch (JsonException)
        {
            return LeaseVerificationResult.Fail("malformed-header");
        }

        if (header is null || string.IsNullOrWhiteSpace(header.Alg) || string.IsNullOrWhiteSpace(header.Kid))
        {
            return LeaseVerificationResult.Fail("missing-header-claims");
        }

        // LSE-2: typ MUST be symlease+jwt
        if (!string.Equals(header.Typ, "symlease+jwt", StringComparison.Ordinal))
        {
            return LeaseVerificationResult.Fail($"invalid-typ:{header.Typ}");
        }

        // 2. Look up key
        if (!_keyRing.TryGet(header.Kid, header.Alg, out var key))
        {
            return LeaseVerificationResult.Fail($"unknown-kid:{header.Kid}");
        }

        if (_keyRing.IsRevoked(header.Kid))
        {
            return LeaseVerificationResult.Fail($"revoked-kid:{header.Kid}");
        }

        // 3. Verify signature (LSE-11)
        byte[] signingInput = Encoding.ASCII.GetBytes($"{parts[0]}.{parts[1]}");
        byte[] signatureBytes;
        try
        {
            signatureBytes = Base64Url.DecodeFromChars(parts[2]);
        }
        catch (FormatException)
        {
            return LeaseVerificationResult.Fail("bad-signature-format");
        }

        if (!key.Verify(signingInput, signatureBytes))
        {
            return LeaseVerificationResult.Fail("bad-signature");
        }

        // 4. Parse payload
        LeaseClaims? claims;
        try
        {
            byte[] payloadBytes = Base64Url.DecodeFromChars(parts[1]);
            claims = JsonSerializer.Deserialize(payloadBytes, AchillesProtocolJsonContext.Default.LeaseClaims);
        }
        catch (FormatException)
        {
            return LeaseVerificationResult.Fail("malformed-payload");
        }
        catch (JsonException)
        {
            return LeaseVerificationResult.Fail("malformed-payload");
        }

        if (claims is null)
        {
            return LeaseVerificationResult.Fail("malformed-payload");
        }

        // 5. Time validation
        long now = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        long skew = (long)(clockSkew ?? TimeSpan.FromMinutes(1)).TotalSeconds;

        if (now > claims.Exp + skew)
        {
            return LeaseVerificationResult.Fail("expired");
        }

        if (now < claims.Iat - skew)
        {
            return LeaseVerificationResult.Fail("not-yet-valid");
        }

        // 6. Monotonic sequence check (LSE-5)
        if (lastSeenSeq > 0 && claims.Seq <= lastSeenSeq)
        {
            return LeaseVerificationResult.Fail("stale-sequence");
        }

        // 7. Fingerprint check (LSE-12)
        if (expectedFpHash is not null && !string.Equals(claims.Fp, expectedFpHash, StringComparison.OrdinalIgnoreCase))
        {
            return LeaseVerificationResult.Fail("fingerprint-mismatch");
        }

        // 8. License ID check (LSE-13)
        if (expectedLicenseId is not null && !string.Equals(claims.Sub, expectedLicenseId, StringComparison.Ordinal))
        {
            return LeaseVerificationResult.Fail("license-mismatch");
        }

        return LeaseVerificationResult.Success(claims);
    }
}
