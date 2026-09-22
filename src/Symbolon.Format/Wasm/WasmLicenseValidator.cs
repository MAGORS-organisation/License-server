using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Symbolon.Crypto;

namespace Symbolon.Format.Wasm;

public sealed record WasmValidationResult(
    [property: JsonPropertyName("isValid")] bool IsValid,
    [property: JsonPropertyName("failureReason")] string? FailureReason,
    [property: JsonPropertyName("customer")] string? Customer,
    [property: JsonPropertyName("product")] string? Product,
    [property: JsonPropertyName("licenseType")] string? LicenseType,
    [property: JsonPropertyName("maxSeats")] int? MaxSeats,
    [property: JsonPropertyName("expiresAt")] long? ExpiresAt,
    [property: JsonPropertyName("isExpired")] bool IsExpired,
    [property: JsonPropertyName("daysRemaining")] int? DaysRemaining,
    [property: JsonPropertyName("machineMatch")] bool MachineMatch,
    [property: JsonPropertyName("features")] IReadOnlyList<string>? Features,
    [property: JsonPropertyName("verifiedAlgs")] IReadOnlyList<string>? VerifiedAlgs);

/// <summary>
/// Lightweight, self-contained license validation engine optimized for WASM / edge execution.
/// Conforms to Phase 2.5 WebAssembly offline license validation specification.
/// </summary>
public static class WasmLicenseValidator
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static string ValidateLicenseJson(
        string pemOrJws,
        string jwksJson,
        string? expectedAudience = null,
        string? fingerprint = null,
        long? currentUnixTimeSeconds = null)
    {
        var result = Validate(pemOrJws, jwksJson, expectedAudience, fingerprint, currentUnixTimeSeconds);
        return JsonSerializer.Serialize(result, JsonOpts);
    }

#pragma warning disable CA1031 // Resilient offline validation parser catches formatting exceptions
    public static WasmValidationResult Validate(
        string pemOrJws,
        string jwksJson,
        string? expectedAudience = null,
        string? fingerprint = null,
        long? currentUnixTimeSeconds = null)
    {
        if (string.IsNullOrWhiteSpace(pemOrJws))
        {
            return new WasmValidationResult(false, "License payload is empty.", null, null, null, null, null, true, null, false, null, null);
        }

        long nowSec = currentUnixTimeSeconds ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // 1. Build KeyRing from JWKS
        using var keyRing = new SymbolonKeyRing();
        var loadedAlgs = new HashSet<string>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(jwksJson))
        {
            try
            {
                using var jwksDoc = JsonDocument.Parse(jwksJson);
                if (jwksDoc.RootElement.TryGetProperty("keys", out var keysElement) && keysElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var keyElem in keysElement.EnumerateArray())
                    {
                        string? kty = keyElem.TryGetProperty("kty", out var ktyElem) ? ktyElem.GetString() : null;
                        string? crv = keyElem.TryGetProperty("crv", out var crvElem) ? crvElem.GetString() : null;
                        string kid = (keyElem.TryGetProperty("kid", out var kidElem) ? kidElem.GetString() : null) ?? "key-wasm";
                        string? x = keyElem.TryGetProperty("x", out var xElem) ? xElem.GetString() : null;
                        string? y = keyElem.TryGetProperty("y", out var yElem) ? yElem.GetString() : null;

                        string? alg = keyElem.TryGetProperty("alg", out var algElem) ? algElem.GetString() : null;
                        string? pub = keyElem.TryGetProperty("pub", out var pubElem) ? pubElem.GetString() : null;

                        if (string.Equals(kty, "EC", StringComparison.OrdinalIgnoreCase) &&
                            string.Equals(crv, "P-256", StringComparison.OrdinalIgnoreCase) &&
                            x is not null && y is not null)
                        {
                            byte[] xBytes = Base64Url.DecodeFromChars(x);
                            byte[] yBytes = Base64Url.DecodeFromChars(y);

                            var prov = Es256SignatureProvider.ImportPublic(xBytes, yBytes, kid);
                            keyRing.Add(prov);
                            loadedAlgs.Add(prov.Alg);
                        }
                        else if ((string.Equals(kty, "AKP", StringComparison.OrdinalIgnoreCase) ||
                                  string.Equals(alg, Alg.MlDsa65, StringComparison.OrdinalIgnoreCase)) &&
                                 pub is not null && MlDsaSignatureProvider.IsSupported)
                        {
                            var jwkDto = new JsonWebKeyDto
                            {
                                Kty = kty ?? "AKP",
                                Alg = alg ?? Alg.MlDsa65,
                                Kid = kid,
                                Pub = pub
                            };
                            var prov = MlDsaSignatureProvider.ImportJwk(jwkDto);
                            keyRing.Add(prov);
                            loadedAlgs.Add(prov.Alg);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                return new WasmValidationResult(false, $"Failed to parse JWKS: {ex.Message}", null, null, null, null, null, true, null, false, null, null);
            }
        }

        // 2. Unarmor if PEM
        string jws = pemOrJws.Trim();
        if (pemOrJws.Contains("-----BEGIN", StringComparison.Ordinal))
        {
            if (PemArmor.TryUnwrap(pemOrJws, "SYMBOLON LICENSE KEY", out byte[]? rawKey))
            {
                jws = Encoding.UTF8.GetString(rawKey).Trim();
            }
            else if (PemArmor.TryUnwrap(pemOrJws, "SYMBOLON LICENSE", out byte[]? rawLic))
            {
                jws = Encoding.UTF8.GetString(rawLic).Trim();
            }
            else
            {
                return new WasmValidationResult(false, "Invalid PEM armor envelope.", null, null, null, null, null, true, null, false, null, null);
            }
        }

        // 3. Verify JWS Document
        var options = new SymbolonVerifierOptions
        {
            ExpectedAudience = expectedAudience,
            ClockSkewTolerance = TimeSpan.FromMinutes(5),
            SupportedAlgs = loadedAlgs.Count > 0 ? loadedAlgs : new HashSet<string>(StringComparer.Ordinal) { Alg.Es256 },
            Strictness = Strictness.Lenient
        };

        var verifier = new LicenseDocumentVerifier(keyRing, TimeProvider.System, options);
        var res = verifier.Verify(jws);

        if (!res.IsValid || res.Claims is null)
        {
            return new WasmValidationResult(false, res.FailureReason ?? "Verification failed.", null, null, null, null, null, true, null, false, null, res.VerifiedAlgs);
        }

        var claims = res.Claims;
        bool isExpired = claims.Exp < nowSec;
        int? daysRemaining = (int)Math.Max(0, (claims.Exp - nowSec) / 86400);

        string customer = claims.Symlic.License.Customer?.Name ?? claims.Symlic.License.Customer?.Ref ?? "Unknown";
        string product = claims.Aud;
        string licenseType = claims.Symlic.License.Model;
        int? maxSeats = claims.Symlic.Limits.MaxSeats;
        var features = claims.Symlic.Entitlements.Select(e => e.Code).ToList();

        // 4. Check hardware fingerprint if node-locked
        bool machineMatch = true;
        string? boundFp = claims.Symlic.Binding?.Fingerprint;
        if (!string.IsNullOrWhiteSpace(fingerprint) && !string.IsNullOrWhiteSpace(boundFp))
        {
            machineMatch = string.Equals(fingerprint.Trim(), boundFp.Trim(), StringComparison.OrdinalIgnoreCase);
            if (!machineMatch)
            {
                return new WasmValidationResult(
                    false,
                    "Machine fingerprint mismatch for node-locked license.",
                    customer,
                    product,
                    licenseType,
                    maxSeats,
                    claims.Exp,
                    isExpired,
                    daysRemaining,
                    false,
                    features,
                    res.VerifiedAlgs);
            }
        }

        if (isExpired)
        {
            return new WasmValidationResult(
                false,
                "License has expired.",
                customer,
                product,
                licenseType,
                maxSeats,
                claims.Exp,
                true,
                0,
                machineMatch,
                features,
                res.VerifiedAlgs);
        }

        return new WasmValidationResult(
            true,
            null,
            customer,
            product,
            licenseType,
            maxSeats,
            claims.Exp,
            false,
            daysRemaining,
            machineMatch,
            features,
            res.VerifiedAlgs);
    }
#pragma warning restore CA1031
}
