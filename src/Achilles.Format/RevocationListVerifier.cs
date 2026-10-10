using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using Achilles.Crypto;

namespace Achilles.Format;

public sealed record RevocationVerificationResult(
    bool IsValid,
    RevocationListClaims? Claims,
    IReadOnlyList<string> VerifiedAlgs,
    string? FailureReason)
{
    public static RevocationVerificationResult Success(
        RevocationListClaims claims,
        IReadOnlyList<string> verifiedAlgs) =>
        new(true, claims, verifiedAlgs, null);

    public static RevocationVerificationResult Fail(string reason) =>
        new(false, null, [], reason);
}

public sealed class RevocationVerifierOptions
{
    public Strictness Strictness { get; init; } = Strictness.Auto;
    public TimeSpan ClockSkewTolerance { get; init; } = TimeSpan.FromMinutes(5);
    public TimeSpan MaxAge { get; init; } = TimeSpan.FromDays(7);
    public long LastSeenSeq { get; init; }
}

/// <summary>
/// Verifies Symbolon Revocation List documents (.symrl) conforming to spec/06-revocation-list.md.
/// </summary>
public sealed class RevocationListVerifier
{
    private readonly IKeyRing _keyRing;
    private readonly TimeProvider _timeProvider;
    private readonly RevocationVerifierOptions _options;

    public RevocationListVerifier(
        IKeyRing keyRing,
        TimeProvider? timeProvider = null,
        RevocationVerifierOptions? options = null)
    {
        _keyRing = keyRing ?? throw new ArgumentNullException(nameof(keyRing));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _options = options ?? new RevocationVerifierOptions();
    }

    /// <summary>
    /// Verifies the revocation list document against RFC 7515 and spec/06-revocation-list.md rules.
    /// </summary>
    public RevocationVerificationResult Verify(string input, string expectedLabel = RevocationListSigner.DefaultPemLabel)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return RevocationVerificationResult.Fail("empty-input");
        }

        // 1. Unwrap PEM or parse bare JSON
        byte[] rawBytes;
        if (PemArmor.TryUnwrap(input, expectedLabel, out byte[]? unwrapped))
        {
            rawBytes = unwrapped;
        }
        else
        {
            string trimmed = input.Trim();
            if (trimmed.StartsWith('{'))
            {
                rawBytes = Encoding.UTF8.GetBytes(trimmed);
            }
            else
            {
                return RevocationVerificationResult.Fail("malformed-pem");
            }
        }

        JwsGeneralJson? doc;
        try
        {
            doc = JsonSerializer.Deserialize(rawBytes, AchillesJsonContext.Default.JwsGeneralJson);
        }
        catch (JsonException)
        {
            return RevocationVerificationResult.Fail("malformed-jws-json");
        }

        if (doc is null || string.IsNullOrWhiteSpace(doc.Payload) || doc.Signatures is null || doc.Signatures.Count == 0)
        {
            return RevocationVerificationResult.Fail("invalid-jws-structure");
        }

        // 2. Decode claims payload
        byte[] payloadBytes;
        try
        {
            payloadBytes = Base64Url.DecodeFromChars(doc.Payload);
        }
        catch (FormatException)
        {
            return RevocationVerificationResult.Fail("malformed-payload-base64");
        }

        RevocationListClaims? claims;
        try
        {
            claims = JsonSerializer.Deserialize(payloadBytes, AchillesJsonContext.Default.RevocationListClaims);
        }
        catch (JsonException)
        {
            return RevocationVerificationResult.Fail("malformed-claims-json");
        }

        if (claims?.Symrl is null)
        {
            return RevocationVerificationResult.Fail("missing-symrl-claim");
        }

        if (claims.Symrl.V != 1)
        {
            return RevocationVerificationResult.Fail($"unsupported-version:{claims.Symrl.V}");
        }

        // 3. Monotonic sequence check (RVL-7)
        if (_options.LastSeenSeq > 0 && claims.Symrl.Seq < _options.LastSeenSeq)
        {
            return RevocationVerificationResult.Fail($"stale-sequence:{claims.Symrl.Seq}<{_options.LastSeenSeq}");
        }

        // 4. Delta sequence gap check (RVL-8)
        if (!claims.Symrl.Full)
        {
            if (!claims.Symrl.Since.HasValue)
            {
                return RevocationVerificationResult.Fail("delta-missing-since");
            }

            if (_options.LastSeenSeq > 0 && claims.Symrl.Since.Value != _options.LastSeenSeq)
            {
                return RevocationVerificationResult.Fail($"delta-gap:since={claims.Symrl.Since.Value},last_seen={_options.LastSeenSeq}");
            }
        }

        // 5. Time validation (RVL-3, RVL-10)
        long now = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        long skew = (long)_options.ClockSkewTolerance.TotalSeconds;

        if (now < claims.Iat - skew)
        {
            return RevocationVerificationResult.Fail("not-yet-valid");
        }

        if (now > claims.Exp + skew)
        {
            // RVL-10: Trust based on maxAge and strictness (RVL-11)
            long maxAgeSeconds = (long)_options.MaxAge.TotalSeconds;
            if (now > claims.Iat + maxAgeSeconds + skew)
            {
                if (_options.Strictness is Strictness.Strict)
                {
                    return RevocationVerificationResult.Fail("revocation-list-expired-exceeds-max-age");
                }
            }
        }

        // 6. Signature validation (RVL-1, RVL-2, RVL-14)
        var verifiedAlgs = new List<string>(doc.Signatures.Count);
        foreach (var sig in doc.Signatures)
        {
            byte[] headerBytes;
            try
            {
                headerBytes = Base64Url.DecodeFromChars(sig.Protected);
            }
            catch (FormatException)
            {
                return RevocationVerificationResult.Fail("malformed-protected-header-base64");
            }

            JwsProtectedHeader? hdr;
            try
            {
                hdr = JsonSerializer.Deserialize(headerBytes, AchillesJsonContext.Default.JwsProtectedHeader);
            }
            catch (JsonException)
            {
                return RevocationVerificationResult.Fail("malformed-protected-header-json");
            }

            if (hdr is null)
            {
                return RevocationVerificationResult.Fail("null-protected-header");
            }

            // RVL-1: Must be typ symrl+jws
            if (!string.Equals(hdr.Typ, RevocationListSigner.DefaultTyp, StringComparison.OrdinalIgnoreCase))
            {
                return RevocationVerificationResult.Fail($"invalid-typ:{hdr.Typ}");
            }

            // RVL-14: List signed by a revoked kid MUST be rejected
            if (_keyRing.IsRevoked(hdr.Kid))
            {
                return RevocationVerificationResult.Fail($"revoked-signing-kid:{hdr.Kid}");
            }

            if (!_keyRing.TryGet(hdr.Kid, hdr.Alg, out var key))
            {
                // Unrecognized key - skip or fail based on strictness
                continue;
            }

            byte[] signingInput = Encoding.ASCII.GetBytes($"{sig.Protected}.{doc.Payload}");
            byte[] signatureBytes;
            try
            {
                signatureBytes = Base64Url.DecodeFromChars(sig.Signature);
            }
            catch (FormatException)
            {
                return RevocationVerificationResult.Fail($"bad-signature-format:{hdr.Alg}");
            }

            if (!key.Verify(signingInput, signatureBytes))
            {
                return RevocationVerificationResult.Fail($"bad-signature:{hdr.Alg}");
            }

            verifiedAlgs.Add(hdr.Alg);
        }

        if (verifiedAlgs.Count == 0)
        {
            return RevocationVerificationResult.Fail("no-verifiable-signature");
        }

        return RevocationVerificationResult.Success(claims, verifiedAlgs);
    }
}
