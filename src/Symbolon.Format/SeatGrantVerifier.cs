using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using Symbolon.Crypto;

namespace Symbolon.Format;

public sealed record SeatGrantVerificationResult(
    bool IsValid,
    SeatGrantDocumentClaims? Claims,
    IReadOnlyList<string> VerifiedAlgs,
    string? FailureReason)
{
    public static SeatGrantVerificationResult Success(
        SeatGrantDocumentClaims claims,
        IReadOnlyList<string> verifiedAlgs) =>
        new(true, claims, verifiedAlgs, null);

    public static SeatGrantVerificationResult Fail(string reason) =>
        new(false, null, [], reason);
}

public sealed class SeatGrantVerifierOptions
{
    public Strictness Strictness { get; init; } = Strictness.Auto;
    public TimeSpan ClockSkewTolerance { get; init; } = TimeSpan.FromMinutes(5);
    public string? ExpectedRelayId { get; init; }
    public string? ExpectedLicenseId { get; init; }
    public long? MinSeq { get; init; }
}

/// <summary>
/// Verifies Symbolon Delegated Seat Grant documents (.symgrant) conforming to spec/05-seat-grant.md.
/// </summary>
public sealed class SeatGrantVerifier
{
    private readonly IKeyRing _keyRing;
    private readonly TimeProvider _timeProvider;
    private readonly SeatGrantVerifierOptions _options;

    public SeatGrantVerifier(
        IKeyRing keyRing,
        TimeProvider? timeProvider = null,
        SeatGrantVerifierOptions? options = null)
    {
        _keyRing = keyRing ?? throw new ArgumentNullException(nameof(keyRing));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _options = options ?? new SeatGrantVerifierOptions();
    }

    /// <summary>
    /// Verifies the .symgrant document against RFC 7515 and Symbolon seat grant specifications.
    /// </summary>
    public SeatGrantVerificationResult Verify(string input, string expectedLabel = SeatGrantSigner.DefaultPemLabel)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return SeatGrantVerificationResult.Fail("empty-input");
        }

        byte[] rawJson;
        if (PemArmor.TryUnwrap(input, expectedLabel, out byte[]? unwrapped))
        {
            rawJson = unwrapped;
        }
        else
        {
            rawJson = Encoding.UTF8.GetBytes(input.Trim());
        }

        JwsGeneralJson? doc;
        try
        {
            doc = JsonSerializer.Deserialize(rawJson, SymbolonJsonContext.Default.JwsGeneralJson);
        }
        catch (JsonException ex)
        {
            return SeatGrantVerificationResult.Fail($"malformed-json: {ex.Message}");
        }

        if (doc is null || string.IsNullOrWhiteSpace(doc.Payload) || doc.Signatures is null || doc.Signatures.Count == 0)
        {
            return SeatGrantVerificationResult.Fail("invalid-jws-envelope");
        }

        byte[] payloadBytes;
        try
        {
            payloadBytes = Base64Url.DecodeFromChars(doc.Payload);
        }
        catch (FormatException)
        {
            return SeatGrantVerificationResult.Fail("invalid-payload-base64");
        }

        SeatGrantDocumentClaims? claims;
        try
        {
            claims = JsonSerializer.Deserialize(payloadBytes, SymbolonJsonContext.Default.SeatGrantDocumentClaims);
        }
        catch (JsonException ex)
        {
            return SeatGrantVerificationResult.Fail($"malformed-claims: {ex.Message}");
        }

        if (claims is null || claims.Symgrant is null)
        {
            return SeatGrantVerificationResult.Fail("missing-symgrant-claim");
        }

        // 1. Verify signatures
        var verifiedAlgs = new List<string>();
        bool hasClassical = false;
        bool hasPqc = false;

        foreach (var sig in doc.Signatures)
        {
            if (string.IsNullOrWhiteSpace(sig.Protected) || string.IsNullOrWhiteSpace(sig.Signature))
            {
                continue;
            }

            byte[] headerBytes;
            try
            {
                headerBytes = Base64Url.DecodeFromChars(sig.Protected);
            }
            catch (FormatException)
            {
                continue;
            }

            JwsProtectedHeader? header;
            try
            {
                header = JsonSerializer.Deserialize(headerBytes, SymbolonJsonContext.Default.JwsProtectedHeader);
            }
            catch (JsonException)
            {
                continue;
            }

            if (header is null || string.IsNullOrWhiteSpace(header.Alg) || string.IsNullOrWhiteSpace(header.Kid))
            {
                continue;
            }

            if (header.Typ != SeatGrantSigner.DefaultTyp)
            {
                return SeatGrantVerificationResult.Fail($"invalid-typ: expected '{SeatGrantSigner.DefaultTyp}', got '{header.Typ}'");
            }

            if (header.Crit is null || !header.Crit.Contains("symgrant"))
            {
                return SeatGrantVerificationResult.Fail("missing-crit-symgrant");
            }

            if (!_keyRing.TryGet(header.Kid, header.Alg, out var verifier))
            {
                continue;
            }

            if (_keyRing.IsRevoked(header.Kid))
            {
                return SeatGrantVerificationResult.Fail($"revoked-kid:{header.Kid}");
            }

            byte[] sigBytes;
            try
            {
                sigBytes = Base64Url.DecodeFromChars(sig.Signature);
            }
            catch (FormatException)
            {
                continue;
            }

            string signingInputStr = $"{sig.Protected}.{doc.Payload}";
            byte[] signingInputBytes = Encoding.ASCII.GetBytes(signingInputStr);

            if (verifier.Verify(signingInputBytes, sigBytes))
            {
                verifiedAlgs.Add(header.Alg);
                if (header.Alg == Alg.Es256)
                {
                    hasClassical = true;
                }
                else if (header.Alg is Alg.MlDsa44 or Alg.MlDsa65 or Alg.MlDsa87)
                {
                    hasPqc = true;
                }
            }
        }

        // Check strictness requirement
        switch (_options.Strictness)
        {
            case Strictness.Strict:
                if (!hasClassical || !hasPqc) return SeatGrantVerificationResult.Fail("hybrid-signature-incomplete");
                break;
            case Strictness.Auto:
            case Strictness.Lenient:
            default:
                if (!hasClassical && !hasPqc) return SeatGrantVerificationResult.Fail("no-valid-signature-found");
                break;
        }

        // 2. Temporal validation (GNT-6)
        long now = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        long skewSec = (long)_options.ClockSkewTolerance.TotalSeconds;

        if (now < claims.Nbf - skewSec)
        {
            return SeatGrantVerificationResult.Fail($"grant-not-yet-valid: nbf={claims.Nbf}, now={now}");
        }

        if (now > claims.Exp + skewSec)
        {
            return SeatGrantVerificationResult.Fail($"grant-expired: exp={claims.Exp}, now={now}");
        }

        // 3. Structural & Disjunction validation (GNT-3)
        if (claims.Symgrant.V != 1)
        {
            return SeatGrantVerificationResult.Fail($"unsupported-version: {claims.Symgrant.V}");
        }

        if (claims.Symgrant.Seats <= 0)
        {
            return SeatGrantVerificationResult.Fail($"invalid-seats-count: {claims.Symgrant.Seats}");
        }

        if (claims.Symgrant.SeatRange is null || claims.Symgrant.SeatRange.Count != 2)
        {
            return SeatGrantVerificationResult.Fail("invalid-seat-range-format");
        }

        int seatFrom = claims.Symgrant.SeatRange[0];
        int seatTo = claims.Symgrant.SeatRange[1];
        if (seatTo < seatFrom)
        {
            return SeatGrantVerificationResult.Fail($"inverted-seat-range: [{seatFrom}, {seatTo}]");
        }

        int rangeCount = seatTo - seatFrom + 1;
        if (rangeCount != claims.Symgrant.Seats)
        {
            return SeatGrantVerificationResult.Fail(
                $"seat-range-disjunction-violation: range count {rangeCount} does not match seats {claims.Symgrant.Seats} (GNT-3)");
        }

        // 4. Expected Relay ID (Audience check)
        if (!string.IsNullOrWhiteSpace(_options.ExpectedRelayId) &&
            !string.Equals(claims.Aud, _options.ExpectedRelayId, StringComparison.Ordinal))
        {
            return SeatGrantVerificationResult.Fail($"audience-mismatch: expected '{_options.ExpectedRelayId}', got '{claims.Aud}'");
        }

        // 5. Expected License ID (Subject check)
        if (!string.IsNullOrWhiteSpace(_options.ExpectedLicenseId) &&
            !string.Equals(claims.Sub, _options.ExpectedLicenseId, StringComparison.Ordinal))
        {
            return SeatGrantVerificationResult.Fail($"subject-mismatch: expected '{_options.ExpectedLicenseId}', got '{claims.Sub}'");
        }

        // 6. Monotonic Sequence Validation (GNT-7: seq > last_seq)
        if (_options.MinSeq.HasValue && claims.Symgrant.Seq <= _options.MinSeq.Value)
        {
            return SeatGrantVerificationResult.Fail(
                $"sequence-rollback-detected: seq {claims.Symgrant.Seq} is not strictly greater than lastSeq {_options.MinSeq.Value} (GNT-7)");
        }

        return SeatGrantVerificationResult.Success(claims, verifiedAlgs);
    }
}
