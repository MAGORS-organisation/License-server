using System.Buffers.Text;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Achilles.Crypto;
using Achilles.Protocol;

namespace Achilles.Format;

public enum Strictness
{
    /// <summary>Default: rejects unsupported required algorithms if document lifetime exceeds 1 year (LIC-25).</summary>
    Auto,
    /// <summary>Strict: always rejects if any required algorithm is unsupported.</summary>
    Strict,
    /// <summary>Lenient: accepts unsupported algorithms during migration windows.</summary>
    Lenient
}

public sealed class AchillesVerifierOptions
{
    public Strictness Strictness { get; init; } = Strictness.Auto;
    public IReadOnlySet<string> SupportedAlgs { get; init; } = new HashSet<string>(StringComparer.Ordinal)
    {
        Alg.Es256,
        Alg.MlDsa65
    };
    public TimeSpan ClockSkewTolerance { get; init; } = TimeSpan.FromMinutes(5);
    public long HighWaterMarkIat { get; init; }
    public string? ExpectedAudience { get; init; }

    /// <summary>
    /// Optional revocation predicate or lookup to reject revoked license IDs (LIC-31).
    /// </summary>
    public Func<string, bool>? IsLicenseRevoked { get; init; }

    /// <summary>
    /// Optional client machine components to verify against license binding claim (LIC-33, FPR-5 to FPR-9).
    /// </summary>
    public IReadOnlyDictionary<string, string>? ClientFingerprintComponents { get; init; }

    /// <summary>
    /// Optional client machine fingerprint hash to verify against license binding claim (LIC-33).
    /// </summary>
    public string? ClientFingerprintHash { get; init; }
}

public sealed record VerificationResult(
    bool IsValid,
    LicenseClaims? Claims,
    IReadOnlyList<string> VerifiedAlgs,
    IReadOnlyList<string> UnverifiableAlgs,
    string? FailureReason)
{
    public static VerificationResult Success(
        LicenseClaims claims,
        IReadOnlyList<string> verifiedAlgs,
        IReadOnlyList<string> unverifiableAlgs) =>
        new(true, claims, verifiedAlgs, unverifiableAlgs, null);

    public static VerificationResult Fail(string reason) =>
        new(false, null, [], [], reason);
}

/// <summary>
/// Verifies Symbolon License Files conforming to normative validation sequence in spec/03-symlic-1.md (LIC-21 to LIC-34).
/// </summary>
public sealed class LicenseDocumentVerifier
{
    private readonly IKeyRing _keyRing;
    private readonly TimeProvider _timeProvider;
    private readonly AchillesVerifierOptions _options;

    public LicenseDocumentVerifier(
        IKeyRing keyRing,
        TimeProvider? timeProvider = null,
        AchillesVerifierOptions? options = null)
    {
        _keyRing = keyRing ?? throw new ArgumentNullException(nameof(keyRing));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _options = options ?? new AchillesVerifierOptions();
    }

    public LicenseDocumentVerifier(
        IKeyRing keyRing,
        AchillesVerifierOptions options)
        : this(keyRing, null, options)
    {
    }

    /// <summary>
    /// Executes the 7-step validation pipeline defined by LIC-34.
    /// </summary>
    public VerificationResult Verify(string input, string expectedLabel = "SYMBOLON LICENSE")
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return VerificationResult.Fail("empty-input");
        }

        // ---- Step 1: PEM unwrap and JWS parsing (LIC-1 to LIC-5) ------------
        byte[] rawBytes;
        if (PemArmor.TryUnwrap(input, expectedLabel, out byte[]? unwrapped))
        {
            rawBytes = unwrapped;
        }
        else
        {
            string trimmed = input.Trim();
            // LIC-4: Verifier MUST accept bare JWS JSON without PEM armor
            if (trimmed.StartsWith('{'))
            {
                rawBytes = Encoding.UTF8.GetBytes(trimmed);
            }
            else
            {
                return VerificationResult.Fail("malformed-pem");
            }
        }

        JwsGeneralJson? doc;
        try
        {
            doc = JsonSerializer.Deserialize(rawBytes, AchillesJsonContext.Default.JwsGeneralJson);
        }
        catch (JsonException)
        {
            return VerificationResult.Fail("malformed-jws-json");
        }

        if (doc is null || doc.Signatures is null || doc.Signatures.Count == 0)
        {
            return VerificationResult.Fail("no-signatures"); // LIC-6
        }

        // Decode payload and deserialize claims
        LicenseClaims? claims;
        try
        {
            byte[] payloadBytes = Base64Url.DecodeFromChars(doc.Payload);
            claims = JsonSerializer.Deserialize(payloadBytes, AchillesJsonContext.Default.LicenseClaims);
        }
        catch (FormatException)
        {
            return VerificationResult.Fail("malformed-payload");
        }
        catch (JsonException)
        {
            return VerificationResult.Fail("malformed-payload");
        }

        if (claims is null)
        {
            return VerificationResult.Fail("malformed-payload");
        }

        // ---- Step 2: Protected headers & crit validation (LIC-7 to LIC-11, LIC-15) -
        var parsedHeaders = new List<JwsProtectedHeader>(doc.Signatures.Count);
        foreach (var sig in doc.Signatures)
        {
            JwsProtectedHeader? header;
            try
            {
                byte[] headerBytes = Base64Url.DecodeFromChars(sig.Protected);
                header = JsonSerializer.Deserialize(headerBytes, AchillesJsonContext.Default.JwsProtectedHeader);
            }
            catch (FormatException)
            {
                return VerificationResult.Fail("malformed-protected-header");
            }
            catch (JsonException)
            {
                return VerificationResult.Fail("malformed-protected-header");
            }

            if (header is null || string.IsNullOrWhiteSpace(header.Alg) || string.IsNullOrWhiteSpace(header.Kid))
            {
                return VerificationResult.Fail("missing-header-parameters");
            }

            // LIC-8: typ MUST be symlic+jws
            if (!string.Equals(header.Typ, "symlic+jws", StringComparison.Ordinal))
            {
                return VerificationResult.Fail($"invalid-typ:{header.Typ}");
            }

            // LIC-9: crit MUST contain "symlic" and unknown extensions MUST cause rejection (RFC 7515 §4.1.11)
            if (header.Crit is null || !header.Crit.Contains("symlic"))
            {
                return VerificationResult.Fail("missing-symlic-crit");
            }

            foreach (var extension in header.Crit)
            {
                if (!string.Equals(extension, "symlic", StringComparison.Ordinal))
                {
                    return VerificationResult.Fail($"unknown-crit:{extension}");
                }
            }

            // LIC-15: symlic.v MUST match protected header "symlic"
            string expectedVersionString = claims.Symlic.V.ToString(CultureInfo.InvariantCulture);
            if (!string.Equals(header.Symlic, expectedVersionString, StringComparison.Ordinal))
            {
                return VerificationResult.Fail("symlic-version-mismatch");
            }

            parsedHeaders.Add(header);
        }

        // ---- Step 3: Signature verification (LIC-21 to LIC-28) --------------
        var verifiedAlgs = new List<string>();
        for (int i = 0; i < doc.Signatures.Count; i++)
        {
            var sig = doc.Signatures[i];
            var hdr = parsedHeaders[i];

            if (!_keyRing.TryGet(hdr.Kid, hdr.Alg, out var key))
            {
                // Key not found in local keyring -> cannot verify this algorithm
                continue;
            }

            // LIC-31: Check if key kid is revoked
            if (_keyRing.IsRevoked(hdr.Kid))
            {
                return VerificationResult.Fail($"revoked-kid:{hdr.Kid}");
            }

            byte[] signingInput = Encoding.ASCII.GetBytes($"{sig.Protected}.{doc.Payload}");
            byte[] signatureBytes;
            try
            {
                signatureBytes = Base64Url.DecodeFromChars(sig.Signature);
            }
            catch (FormatException)
            {
                return VerificationResult.Fail($"bad-signature-format:{hdr.Alg}");
            }

            // LIC-23: Invalid signature MUST cause immediate failure, NEVER skip
            if (!key.Verify(signingInput, signatureBytes))
            {
                return VerificationResult.Fail($"bad-signature:{hdr.Alg}");
            }

            verifiedAlgs.Add(hdr.Alg);
        }

        if (verifiedAlgs.Count == 0)
        {
            return VerificationResult.Fail("no-verifiable-signature");
        }

        // Downgrade / signature stripping defense (LIC-22, LIC-23, LIC-25)
        var requiredAlgs = claims.Symlic.RequiredAlgs;
        var missingAlgs = requiredAlgs.Except(verifiedAlgs, StringComparer.Ordinal).ToArray();
        var unverifiableAlgs = missingAlgs.Where(a => !_options.SupportedAlgs.Contains(a)).ToArray();
        var strippedAlgs = missingAlgs.Except(unverifiableAlgs, StringComparer.Ordinal).ToArray();

        // If an algorithm is supported by us but missing/unverified in document -> stripped signature attack!
        if (strippedAlgs.Length > 0)
        {
            return VerificationResult.Fail($"stripped-signature:{string.Join(',', strippedAlgs)}");
        }

        // If an algorithm is not supported by us at all -> policy determines behavior (LIC-25)
        if (unverifiableAlgs.Length > 0)
        {
            bool longLived = (claims.Exp - claims.Iat) > (365L * 24 * 3600);
            if (_options.Strictness is Strictness.Strict || (longLived && _options.Strictness is Strictness.Auto))
            {
                return VerificationResult.Fail($"unsupported-required-alg:{string.Join(',', unverifiableAlgs)}");
            }
        }

        // ---- Step 4: Time validation (LIC-29, LIC-30) ------------------------
        long now = _timeProvider.GetUtcNow().ToUnixTimeSeconds();
        TimeSpan skewTolerance = claims.Symlic.Policy?.GetClockSkewToleranceOrDefault() ?? _options.ClockSkewTolerance;
        long skew = (long)skewTolerance.TotalSeconds;

        long effectiveNbf = claims.Nbf ?? claims.Iat;
        if (now < effectiveNbf - skew)
        {
            return VerificationResult.Fail("not-yet-valid");
        }

        if (now > claims.Exp + skew)
        {
            return VerificationResult.Fail("expired");
        }

        // LIC-30: Monotonic high-water mark defense against replay of older documents
        if (_options.HighWaterMarkIat > 0 && claims.Iat < _options.HighWaterMarkIat - skew)
        {
            return VerificationResult.Fail("replayed-older-document");
        }

        // ---- Step 5: License state & audience (LIC-13, LIC-17, LIC-31) ------
        if (_options.IsLicenseRevoked is not null &&
            (_options.IsLicenseRevoked(claims.Sub) ||
             (claims.Symlic.License.Key is not null && _options.IsLicenseRevoked(claims.Symlic.License.Key))))
        {
            return VerificationResult.Fail($"revoked-license:{claims.Sub}");
        }

        if (!string.Equals(claims.Symlic.License.State, "active", StringComparison.OrdinalIgnoreCase))
        {
            return VerificationResult.Fail($"license-not-active:{claims.Symlic.License.State}");
        }

        if (_options.ExpectedAudience is not null &&
            !string.Equals(claims.Aud, _options.ExpectedAudience, StringComparison.Ordinal))
        {
            return VerificationResult.Fail($"audience-mismatch:{claims.Aud}");
        }

        // ---- Step 7: Machine binding verification (LIC-33, LIC-34) -----------
        var binding = claims.Symlic.Binding;
        if (binding is not null)
        {
            if (_options.ClientFingerprintComponents is not null && binding.Components is not null && binding.Components.Count > 0)
            {
                var matchResult = FingerprintMatchingEngine.EvaluateMatch(
                    binding.Components,
                    _options.ClientFingerprintComponents,
                    binding.Matching);

                if (!matchResult.IsMatch)
                {
                    return VerificationResult.Fail($"binding-mismatch:{matchResult.FailureReason ?? "Fingerprint components do not match license binding (LIC-33)"}");
                }
            }
            else if (!string.IsNullOrWhiteSpace(_options.ClientFingerprintHash) && !string.IsNullOrWhiteSpace(binding.Fingerprint))
            {
                if (!string.Equals(_options.ClientFingerprintHash.Trim(), binding.Fingerprint.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    return VerificationResult.Fail($"binding-mismatch:Client fingerprint hash does not match license binding ({_options.ClientFingerprintHash} != {binding.Fingerprint})");
                }
            }
        }

        return VerificationResult.Success(claims, verifiedAlgs, unverifiableAlgs);
    }
}
