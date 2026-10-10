using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using Achilles.Crypto;

namespace Achilles.Format;

public sealed record SymleaseVerificationResult(
    bool IsValid,
    SymleaseClaims? Claims,
    IReadOnlyList<string> VerifiedAlgs,
    string? FailureReason)
{
    public static SymleaseVerificationResult Success(
        SymleaseClaims claims,
        IReadOnlyList<string> verifiedAlgs) =>
        new(true, claims, verifiedAlgs, null);

    public static SymleaseVerificationResult Fail(string reason) =>
        new(false, null, [], reason);
}

public sealed class SymleaseVerifierOptions
{
    public Strictness Strictness { get; init; } = Strictness.Auto;
    public TimeSpan ClockSkewTolerance { get; init; } = TimeSpan.FromMinutes(5);
    public string? ExpectedFingerprint { get; init; }
    public string? ExpectedLicenseId { get; init; }
}

/// <summary>
/// Verifies Symbolon Offline Roaming Borrow Lease documents (.symlease) conforming to spec/07-floating-protokol.md §7.4.
/// </summary>
public sealed class SymleaseVerifier
{
    private readonly IKeyRing _keyRing;
    private readonly TimeProvider _timeProvider;
    private readonly SymleaseVerifierOptions _options;

    public SymleaseVerifier(
        IKeyRing keyRing,
        TimeProvider? timeProvider = null,
        SymleaseVerifierOptions? options = null)
    {
        _keyRing = keyRing ?? throw new ArgumentNullException(nameof(keyRing));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _options = options ?? new SymleaseVerifierOptions();
    }

    /// <summary>
    /// Verifies the .symlease document against RFC 7515 and Symbolon offline borrow specifications.
    /// </summary>
    public SymleaseVerificationResult Verify(string input, string expectedLabel = SymleaseSigner.DefaultPemLabel)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return SymleaseVerificationResult.Fail("empty-input");
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
            doc = JsonSerializer.Deserialize(rawJson, AchillesJsonContext.Default.JwsGeneralJson);
        }
        catch (JsonException ex)
        {
            return SymleaseVerificationResult.Fail($"malformed-json: {ex.Message}");
        }

        if (doc is null || string.IsNullOrWhiteSpace(doc.Payload) || doc.Signatures is null || doc.Signatures.Count == 0)
        {
            return SymleaseVerificationResult.Fail("invalid-jws-envelope");
        }

        byte[] payloadBytes;
        try
        {
            payloadBytes = Base64Url.DecodeFromChars(doc.Payload);
        }
        catch (FormatException)
        {
            return SymleaseVerificationResult.Fail("invalid-payload-base64");
        }

        SymleaseClaims? claims;
        try
        {
            claims = JsonSerializer.Deserialize(payloadBytes, AchillesJsonContext.Default.SymleaseClaims);
        }
        catch (JsonException ex)
        {
            return SymleaseVerificationResult.Fail($"malformed-claims: {ex.Message}");
        }

        if (claims is null)
        {
            return SymleaseVerificationResult.Fail("empty-claims");
        }

        // Verify time validity
        long now = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        long skew = (long)_options.ClockSkewTolerance.TotalSeconds;

        if (claims.Exp + skew < now)
        {
            return SymleaseVerificationResult.Fail("symlease-expired");
        }

        if (claims.Nbf - skew > now)
        {
            return SymleaseVerificationResult.Fail("symlease-not-yet-valid");
        }

        if (_options.ExpectedLicenseId is not null && !string.Equals(claims.Sub, _options.ExpectedLicenseId, StringComparison.OrdinalIgnoreCase))
        {
            return SymleaseVerificationResult.Fail("license-id-mismatch");
        }

        if (_options.ExpectedFingerprint is not null && !string.Equals(claims.Fp, _options.ExpectedFingerprint, StringComparison.OrdinalIgnoreCase))
        {
            return SymleaseVerificationResult.Fail("fingerprint-mismatch");
        }

        // Verify signatures
        var verifiedAlgs = new List<string>();
        foreach (var sig in doc.Signatures)
        {
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
                header = JsonSerializer.Deserialize(headerBytes, AchillesJsonContext.Default.JwsProtectedHeader);
            }
            catch (JsonException)
            {
                continue;
            }

            if (header is null || string.IsNullOrWhiteSpace(header.Kid) || string.IsNullOrWhiteSpace(header.Alg))
            {
                continue;
            }

            if (_keyRing.IsRevoked(header.Kid))
            {
                continue;
            }

            if (!_keyRing.TryGet(header.Kid, header.Alg, out var key))
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

            if (key.Verify(signingInputBytes, sigBytes))
            {
                verifiedAlgs.Add(header.Alg);
            }
        }

        if (verifiedAlgs.Count == 0)
        {
            return SymleaseVerificationResult.Fail("no-valid-signature");
        }

        return SymleaseVerificationResult.Success(claims, verifiedAlgs);
    }
}
