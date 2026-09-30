using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using Symbolon.Crypto;

namespace Symbolon.Format;

public sealed record AirGapRequestVerificationResult(
    bool IsValid,
    AirGapRequestClaims? Claims,
    string? FailureReason)
{
    public static AirGapRequestVerificationResult Success(AirGapRequestClaims claims) =>
        new(true, claims, null);

    public static AirGapRequestVerificationResult Fail(string reason) =>
        new(false, null, reason);
}

/// <summary>
/// Verifies Air-Gapped Grant Requests (.symreq) according to spec/07-floating-protokol.md §7.7 (FLT-32, FLT-34).
/// </summary>
public sealed class AirGapRequestVerifier
{
    private readonly IKeyRing? _keyRing;
    private readonly ISignatureProvider? _directVerifier;
    private readonly TimeProvider _timeProvider;

    public AirGapRequestVerifier(IKeyRing keyRing, TimeProvider? timeProvider = null)
    {
        _keyRing = keyRing ?? throw new ArgumentNullException(nameof(keyRing));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public AirGapRequestVerifier(ISignatureProvider directVerifier, TimeProvider? timeProvider = null)
    {
        _directVerifier = directVerifier ?? throw new ArgumentNullException(nameof(directVerifier));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public AirGapRequestVerificationResult Verify(string input, string expectedLabel = AirGapRequestSigner.DefaultPemLabel)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return AirGapRequestVerificationResult.Fail("empty-input");
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
            return AirGapRequestVerificationResult.Fail($"malformed-json: {ex.Message}");
        }

        if (doc is null || string.IsNullOrWhiteSpace(doc.Payload) || doc.Signatures is null || doc.Signatures.Count == 0)
        {
            return AirGapRequestVerificationResult.Fail("invalid-jws-envelope");
        }

        byte[] payloadBytes;
        try
        {
            payloadBytes = Base64Url.DecodeFromChars(doc.Payload);
        }
        catch (FormatException)
        {
            return AirGapRequestVerificationResult.Fail("invalid-payload-base64");
        }

        AirGapRequestClaims? claims;
        try
        {
            claims = JsonSerializer.Deserialize(payloadBytes, SymbolonJsonContext.Default.AirGapRequestClaims);
        }
        catch (JsonException ex)
        {
            return AirGapRequestVerificationResult.Fail($"malformed-claims: {ex.Message}");
        }

        if (claims is null || claims.Symreq is null)
        {
            return AirGapRequestVerificationResult.Fail("missing-symreq-claim");
        }

        // Verify signature
        bool signatureValid = false;
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

            if (header.Typ != AirGapRequestSigner.DefaultTyp)
            {
                return AirGapRequestVerificationResult.Fail($"invalid-typ: expected '{AirGapRequestSigner.DefaultTyp}', got '{header.Typ}'");
            }

            ISignatureProvider? verifier = _directVerifier;
            if (verifier is null && _keyRing is not null)
            {
                _keyRing.TryGet(header.Kid, header.Alg, out verifier);
            }

            if (verifier is null)
            {
                continue;
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
                signatureValid = true;
                break;
            }
        }

        if (!signatureValid)
        {
            return AirGapRequestVerificationResult.Fail("invalid-signature");
        }

        // Temporal validation
        long now = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        if (now > claims.Exp + 300) // 5 min skew tolerance
        {
            return AirGapRequestVerificationResult.Fail($"request-expired: exp={claims.Exp}, now={now}");
        }

        // Structural checks
        if (claims.Symreq.RequestedSeats <= 0)
        {
            return AirGapRequestVerificationResult.Fail("requested-seats-must-be-positive");
        }

        if (string.IsNullOrWhiteSpace(claims.Symreq.UsageDigest))
        {
            return AirGapRequestVerificationResult.Fail("missing-usage-digest");
        }

        if (string.IsNullOrWhiteSpace(claims.Symreq.Nonce))
        {
            return AirGapRequestVerificationResult.Fail("missing-nonce");
        }

        return AirGapRequestVerificationResult.Success(claims);
    }
}
