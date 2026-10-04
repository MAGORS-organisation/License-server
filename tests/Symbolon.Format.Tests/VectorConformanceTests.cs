using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Symbolon.Crypto;
using Xunit;

namespace Symbolon.Format.Tests;

internal sealed record KeyVectorEntry(string Kid, string Alg, JsonWebKeyDto PublicJwk);
internal sealed record VectorExpectedResult(bool IsValid, string? FailureReason, IReadOnlyList<string>? VerifiedAlgs);
internal sealed record ConformanceTestVector(
    string VectorId,
    string Requirement,
    string Description,
    IReadOnlyList<KeyVectorEntry> Keys,
    string DocumentPem,
    long? HighWaterMarkIat,
    IReadOnlyList<string>? RevokedKids,
    VectorExpectedResult Expected
);

public sealed class VectorConformanceTests
{
    private static readonly JsonSerializerOptions JsonIndentOptions = new() { WriteIndented = true };

    private static string GetVectorsDirectory()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            string candidate = Path.Combine(current, "vectors");
            if (Directory.Exists(candidate) || File.Exists(Path.Combine(current, "Symbolon.slnx")))
            {
                return Path.Combine(current, "vectors");
            }
            string? parent = Directory.GetParent(current)?.FullName;
            if (parent == current || parent == null) break;
            current = parent;
        }
        return Path.Combine(AppContext.BaseDirectory, "vectors");
    }

    [Fact]
    public void GenerateAndVerify_AllNormativeVectors_Succeed()
    {
        string vectorsDir = GetVectorsDirectory();
        Directory.CreateDirectory(vectorsDir);

        using var ecKey = Es256SignatureProvider.GenerateKey("prd-acme-2026-ec");
        using var pqKey = MlDsaSignatureProvider.GenerateKey(MLDsaAlgorithm.MLDsa65, "prd-acme-2026-pq");

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // 1. Vector: lic-21-valid-hybrid
        var claimsValid = new LicenseClaims
        {
            Iss = "https://licenses.acme.example",
            Sub = "lic_01JQ8ZK4N9V2X6M0",
            Aud = "acme-cad",
            Jti = "lf_01JQ8ZK5T3P7Q1R4",
            Iat = now,
            Nbf = now,
            Exp = now + 3600 * 24 * 30, // 30 days
            Symlic = new SymlicClaims
            {
                V = 1,
                Profile = "hybrid-v1",
                RequiredAlgs = [Alg.Es256, Alg.MlDsa65],
                License = new LicenseMetadata
                {
                    Key = "SYM-4K7QT-9M2XA-B8ZH3-P6RWY-C1J8N",
                    Model = "floating",
                    State = "active",
                    Customer = new CustomerMetadata { Ref = "CUST-4711", Name = "ACME Corp" }
                },
                Limits = new LicenseLimits { MaxSeats = 10, SeatUnit = "machine" }
            }
        };

        var signerHybrid = new LicenseDocumentSigner([ecKey, pqKey]);
        string pemValid = signerHybrid.Sign(claimsValid);

        var v1 = new ConformanceTestVector(
            VectorId: "lic-21-valid-hybrid",
            Requirement: "LIC-21, LIC-22",
            Description: "Valid hybrid license document signed with both ES256 and ML-DSA-65 algorithms",
            Keys:
            [
                new("prd-acme-2026-ec", Alg.Es256, ecKey.ExportPublicJwk()),
                new("prd-acme-2026-pq", Alg.MlDsa65, pqKey.ExportPublicJwk())
            ],
            DocumentPem: pemValid,
            HighWaterMarkIat: null,
            RevokedKids: null,
            Expected: new(true, null, [Alg.Es256, Alg.MlDsa65])
        );

        // 2. Vector: lic-23-downgrade-attack
        PemArmor.TryUnwrap(pemValid, "SYMBOLON LICENSE", out byte[]? rawJson).Should().BeTrue();
        var jwsDoc = JsonSerializer.Deserialize(rawJson!, SymbolonJsonContext.Default.JwsGeneralJson);
        var strippedSignatures = jwsDoc!.Signatures
            .Where(s =>
            {
                byte[] hdrBytes = Base64Url.DecodeFromChars(s.Protected);
                var hdr = JsonSerializer.Deserialize(hdrBytes, SymbolonJsonContext.Default.JwsProtectedHeader);
                return hdr?.Alg == Alg.Es256;
            })
            .ToList();
        var strippedJws = new JwsGeneralJson(jwsDoc.Payload, strippedSignatures);
        byte[] strippedBytes = JsonSerializer.SerializeToUtf8Bytes(strippedJws, SymbolonJsonContext.Default.JwsGeneralJson);
        string pemStripped = PemArmor.Wrap("SYMBOLON LICENSE", strippedBytes);

        var v2 = new ConformanceTestVector(
            VectorId: "lic-23-downgrade-attack",
            Requirement: "LIC-23",
            Description: "Downgrade attack vector: ML-DSA signature stripped when requiredAlgs demands both ES256 and ML-DSA-65",
            Keys:
            [
                new("prd-acme-2026-ec", Alg.Es256, ecKey.ExportPublicJwk()),
                new("prd-acme-2026-pq", Alg.MlDsa65, pqKey.ExportPublicJwk())
            ],
            DocumentPem: pemStripped,
            HighWaterMarkIat: null,
            RevokedKids: null,
            Expected: new(false, "stripped-signature:ML-DSA-65", null)
        );

        // 3. Vector: lic-25-unknown-alg
        var claimsUnknown = new LicenseClaims
        {
            Iss = "https://licenses.acme.example",
            Sub = "lic_01JQ8ZK4N9V2X6M0",
            Aud = "acme-cad",
            Jti = "lf_01JQ8ZK5T3P7Q1R4",
            Iat = now,
            Nbf = now,
            Exp = now + 3600 * 24 * 730, // 2 years (> 1 year)
            Symlic = new SymlicClaims
            {
                V = 1,
                Profile = "pqc-strict",
                RequiredAlgs = [Alg.Es256, "FALCON-512-UNKNOWN"],
                License = new LicenseMetadata { Key = "SYM-4K7QT-9M2XA-B8ZH3-P6RWY-C1J8N", Model = "floating", State = "active" },
                Limits = new LicenseLimits { MaxSeats = 10, SeatUnit = "machine" }
            }
        };
        var signerEcOnly = new LicenseDocumentSigner([ecKey]);
        string pemUnknown = signerEcOnly.Sign(claimsUnknown);

        var v3 = new ConformanceTestVector(
            VectorId: "lic-25-unknown-alg",
            Requirement: "LIC-25",
            Description: "Unknown algorithm in requiredAlgs for long-lived document evaluated under Strictness.Auto",
            Keys:
            [
                new("prd-acme-2026-ec", Alg.Es256, ecKey.ExportPublicJwk())
            ],
            DocumentPem: pemUnknown,
            HighWaterMarkIat: null,
            RevokedKids: null,
            Expected: new(false, "unsupported-required-alg:FALCON-512-UNKNOWN", null)
        );

        // 4. Vector: lic-15-version-mismatch
        var badVersionHdr = new JwsProtectedHeader(
            Alg: Alg.Es256,
            Kid: "prd-acme-2026-ec",
            Typ: "symlic+jws",
            Crit: ["symlic"],
            Symlic: "2");
        byte[] badHdrBytes = JsonSerializer.SerializeToUtf8Bytes(badVersionHdr, SymbolonJsonContext.Default.JwsProtectedHeader);
        string badHdrB64 = Base64Url.EncodeToString(badHdrBytes);
        byte[] signingInput = Encoding.ASCII.GetBytes($"{badHdrB64}.{jwsDoc.Payload}");
        byte[] badSig = new byte[ecKey.SignatureSize];
        ecKey.Sign(signingInput, badSig);
        var badVersionJws = new JwsGeneralJson(jwsDoc.Payload, [new JwsSignature(badHdrB64, Base64Url.EncodeToString(badSig))]);
        string pemVersionMismatch = PemArmor.Wrap("SYMBOLON LICENSE", JsonSerializer.SerializeToUtf8Bytes(badVersionJws, SymbolonJsonContext.Default.JwsGeneralJson));

        var v4 = new ConformanceTestVector(
            VectorId: "lic-15-version-mismatch",
            Requirement: "LIC-15",
            Description: "Header symlic claim mismatch with payload symlic.v claim",
            Keys:
            [
                new("prd-acme-2026-ec", Alg.Es256, ecKey.ExportPublicJwk())
            ],
            DocumentPem: pemVersionMismatch,
            HighWaterMarkIat: null,
            RevokedKids: null,
            Expected: new(false, "symlic-version-mismatch", null)
        );

        // 5. Vector: lic-30-clock-rollback
        var claimsOlder = new LicenseClaims
        {
            Iss = "https://licenses.acme.example",
            Sub = "lic_01JQ8ZK4N9V2X6M0",
            Aud = "acme-cad",
            Jti = "lf_01JQ8ZK5T3P7Q1R4",
            Iat = now - 7200,
            Exp = now + 3600,
            Symlic = new SymlicClaims
            {
                V = 1,
                Profile = "classical-v1",
                RequiredAlgs = [Alg.Es256],
                License = new LicenseMetadata { Key = "SYM-4K7QT-9M2XA-B8ZH3-P6RWY-C1J8N", Model = "floating", State = "active" },
                Limits = new LicenseLimits { SeatUnit = "machine" }
            }
        };
        string pemOlder = signerEcOnly.Sign(claimsOlder);

        var v5 = new ConformanceTestVector(
            VectorId: "lic-30-clock-rollback",
            Requirement: "LIC-30",
            Description: "Monotonic high-water mark defense against clock rollback and document replay",
            Keys:
            [
                new("prd-acme-2026-ec", Alg.Es256, ecKey.ExportPublicJwk())
            ],
            DocumentPem: pemOlder,
            HighWaterMarkIat: now,
            RevokedKids: null,
            Expected: new(false, "replayed-older-document", null)
        );

        // 6. Vector: lic-31-revoked-kid
        var v6 = new ConformanceTestVector(
            VectorId: "lic-31-revoked-kid",
            Requirement: "LIC-31",
            Description: "Valid cryptographic signature but the signing key kid is revoked",
            Keys:
            [
                new("prd-acme-2026-ec", Alg.Es256, ecKey.ExportPublicJwk())
            ],
            DocumentPem: pemOlder,
            HighWaterMarkIat: null,
            RevokedKids: ["prd-acme-2026-ec"],
            Expected: new(false, "revoked-kid:prd-acme-2026-ec", null)
        );

        var allVectors = new[] { v1, v2, v3, v4, v5, v6 };

        // Write vectors out to JSON files in vectors/
        foreach (var v in allVectors)
        {
            string filePath = Path.Combine(vectorsDir, $"{v.VectorId}.json");
            File.WriteAllText(filePath, JsonSerializer.Serialize(v, JsonIndentOptions));
        }

        // Now run conformance validation on each vector
        foreach (var v in allVectors)
        {
            using var ring = new SymbolonKeyRing();
            foreach (var k in v.Keys)
            {
                if (k.Alg == Alg.Es256)
                {
                    ring.Add(Es256SignatureProvider.ImportJwk(k.PublicJwk));
                }
                else if (k.Alg == Alg.MlDsa65)
                {
                    ring.Add(MlDsaSignatureProvider.ImportJwk(k.PublicJwk));
                }
            }

            if (v.RevokedKids != null)
            {
                foreach (var rk in v.RevokedKids)
                {
                    ring.Revoke(rk);
                }
            }

            var verifierOptions = v.HighWaterMarkIat.HasValue
                ? new SymbolonVerifierOptions { HighWaterMarkIat = v.HighWaterMarkIat.Value }
                : new SymbolonVerifierOptions();

            var verifier = new LicenseDocumentVerifier(ring, options: verifierOptions);
            var result = verifier.Verify(v.DocumentPem);

            result.IsValid.Should().Be(v.Expected.IsValid, $"Vector {v.VectorId} validity failed");
            if (v.Expected.FailureReason != null)
            {
                result.FailureReason.Should().Be(v.Expected.FailureReason, $"Vector {v.VectorId} failure reason mismatch");
            }
            if (v.Expected.VerifiedAlgs != null)
            {
                result.VerifiedAlgs.Should().Contain(v.Expected.VerifiedAlgs);
            }
        }
    }
}
