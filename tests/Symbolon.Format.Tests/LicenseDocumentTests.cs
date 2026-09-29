using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Symbolon.Crypto;
using Xunit;

namespace Symbolon.Format.Tests;

public sealed class LicenseDocumentTests
{
    private static LicenseClaims CreateSampleClaims(
        long nowSeconds,
        string model = "floating",
        string state = "active",
        string aud = "acme-cad")
    {
        return new LicenseClaims
        {
            Iss = "https://licenses.acme.example",
            Sub = "lic_01JQ8ZK4N9V2X6M0",
            Aud = aud,
            Jti = "lf_01JQ8ZK5T3P7Q1R4",
            Iat = nowSeconds,
            Nbf = nowSeconds,
            Exp = nowSeconds + 30 * 24 * 3600, // 30 days
            Symlic = new SymlicClaims
            {
                V = 1,
                Profile = "hybrid-v1",
                RequiredAlgs = [Alg.Es256, Alg.MlDsa65],
                License = new LicenseMetadata
                {
                    Key = "SYM-4K7QT-9M2XA-B8ZH3-P6RWY-C1J8N",
                    Model = model,
                    State = state,
                    ExpiresAt = null,
                    Customer = new CustomerMetadata
                    {
                        Ref = "CUST-4711",
                        Name = "ACME Engineering GmbH"
                    }
                },
                Limits = new LicenseLimits
                {
                    MaxSeats = 25,
                    SeatUnit = "machine",
                    MaxRelays = 3
                },
                Entitlements =
                [
                    new EntitlementClaim { Code = "core" },
                    new EntitlementClaim { Code = "module.cad-export" }
                ],
                Policy = new PolicyClaim
                {
                    ClockSkewTolerance = "PT5M",
                    Lease = new LeasePolicyClaim
                    {
                        Ttl = "PT10M",
                        GraceTtl = "PT4H"
                    }
                }
            }
        };
    }

    [Fact]
    public void HybridLicense_SignAndVerify_Roundtrip_Succeeds()
    {
        using var ecKey = Es256SignatureProvider.GenerateKey("prd-acme-2026-09-ec");
        using var pqKey = MlDsaSignatureProvider.GenerateKey(MLDsaAlgorithm.MLDsa65, "prd-acme-2026-09-pq");

        var signer = new LicenseDocumentSigner([ecKey, pqKey]);

        using var keyRing = new SymbolonKeyRing();
        keyRing.Add(ecKey);
        keyRing.Add(pqKey);

        var verifier = new LicenseDocumentVerifier(keyRing);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims = CreateSampleClaims(now);

        string pem = signer.Sign(claims);
        var result = verifier.Verify(pem);

        result.IsValid.Should().BeTrue();
        result.FailureReason.Should().BeNull();
        result.VerifiedAlgs.Should().Contain([Alg.Es256, Alg.MlDsa65]);
        result.Claims!.Sub.Should().Be("lic_01JQ8ZK4N9V2X6M0");
        result.Claims.Symlic.License.Key.Should().Be("SYM-4K7QT-9M2XA-B8ZH3-P6RWY-C1J8N");
    }

    [Fact]
    public void Verify_StrippingAttack_RemovesMlDsa_RejectsWithStrippedSignature()
    {
        // ATTACK SCENARIO (LIC-23):
        // Attacker intercepts a hybrid license and strips the post-quantum ML-DSA-65 signature,
        // leaving only the classical ES256 signature.
        using var ecKey = Es256SignatureProvider.GenerateKey("prd-acme-2026-09-ec");
        using var pqKey = MlDsaSignatureProvider.GenerateKey(MLDsaAlgorithm.MLDsa65, "prd-acme-2026-09-pq");

        var signer = new LicenseDocumentSigner([ecKey, pqKey]);

        using var keyRing = new SymbolonKeyRing();
        keyRing.Add(ecKey);
        keyRing.Add(pqKey);

        var verifier = new LicenseDocumentVerifier(keyRing);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims = CreateSampleClaims(now);

        string pem = signer.Sign(claims);

        // Unwrap PEM to tamper with the JWS JSON
        PemArmor.TryUnwrap(pem, "SYMBOLON LICENSE", out byte[]? rawJson).Should().BeTrue();
        var jwsDoc = JsonSerializer.Deserialize(rawJson!, SymbolonJsonContext.Default.JwsGeneralJson);
        jwsDoc.Should().NotBeNull();

        // Strip the ML-DSA signature: keep only ES256
        var strippedSignatures = jwsDoc!.Signatures
            .Where(s =>
            {
                byte[] hdrBytes = Base64Url.DecodeFromChars(s.Protected);
                var hdr = JsonSerializer.Deserialize(hdrBytes, SymbolonJsonContext.Default.JwsProtectedHeader);
                return hdr?.Alg == Alg.Es256;
            })
            .ToList();

        var tamperedDoc = new JwsGeneralJson(jwsDoc.Payload, strippedSignatures);
        byte[] tamperedBytes = JsonSerializer.SerializeToUtf8Bytes(tamperedDoc, SymbolonJsonContext.Default.JwsGeneralJson);
        string tamperedPem = PemArmor.Wrap("SYMBOLON LICENSE", tamperedBytes);

        // Verify the tampered document
        var result = verifier.Verify(tamperedPem);

        // Must reject because ML-DSA-65 is in requiredAlgs and supported, but missing!
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be("stripped-signature:ML-DSA-65");
    }

    [Fact]
    public void Verify_CorruptedSignature_RejectsImmediately()
    {
        using var ecKey = Es256SignatureProvider.GenerateKey("prd-acme-2026-09-ec");
        var signer = new LicenseDocumentSigner([ecKey]);

        using var keyRing = new SymbolonKeyRing();
        keyRing.Add(ecKey);

        var verifier = new LicenseDocumentVerifier(keyRing);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims = new LicenseClaims
        {
            Iss = "https://licenses.acme.example",
            Sub = "lic_01JQ8ZK4N9V2X6M0",
            Aud = "acme-cad",
            Jti = "lf_01JQ8ZK5T3P7Q1R4",
            Iat = now,
            Exp = now + 3600,
            Symlic = new SymlicClaims
            {
                V = 1,
                Profile = "classical-v1",
                RequiredAlgs = [Alg.Es256],
                License = new LicenseMetadata { Model = "floating", State = "active" },
                Limits = new LicenseLimits { SeatUnit = "machine" }
            }
        };

        string pem = signer.Sign(claims);

        PemArmor.TryUnwrap(pem, "SYMBOLON LICENSE", out byte[]? rawJson).Should().BeTrue();
        var jwsDoc = JsonSerializer.Deserialize(rawJson!, SymbolonJsonContext.Default.JwsGeneralJson)!;

        // Corrupt the signature bytes
        byte[] sigBytes = Base64Url.DecodeFromChars(jwsDoc.Signatures[0].Signature);
        sigBytes[0] ^= 0xFF;
        var badSig = new JwsSignature(jwsDoc.Signatures[0].Protected, Base64Url.EncodeToString(sigBytes));

        var tamperedDoc = new JwsGeneralJson(jwsDoc.Payload, [badSig]);
        string tamperedPem = PemArmor.Wrap("SYMBOLON LICENSE", JsonSerializer.SerializeToUtf8Bytes(tamperedDoc, SymbolonJsonContext.Default.JwsGeneralJson));

        var result = verifier.Verify(tamperedPem);
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be("bad-signature:ES256");
    }

    [Fact]
    public void Verify_RevokedKey_FailsValidation()
    {
        using var ecKey = Es256SignatureProvider.GenerateKey("revoked-ec-kid");
        var signer = new LicenseDocumentSigner([ecKey]);

        using var keyRing = new SymbolonKeyRing();
        keyRing.Add(ecKey);
        keyRing.Revoke("revoked-ec-kid"); // explicitly revoke

        var verifier = new LicenseDocumentVerifier(keyRing);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims = new LicenseClaims
        {
            Iss = "https://licenses.acme.example",
            Sub = "lic_01JQ8ZK4N9V2X6M0",
            Aud = "acme-cad",
            Jti = "lf_01JQ8ZK5T3P7Q1R4",
            Iat = now,
            Exp = now + 3600,
            Symlic = new SymlicClaims
            {
                V = 1,
                Profile = "classical-v1",
                RequiredAlgs = [Alg.Es256],
                License = new LicenseMetadata { Model = "floating", State = "active" },
                Limits = new LicenseLimits { SeatUnit = "machine" }
            }
        };

        string pem = signer.Sign(claims);
        var result = verifier.Verify(pem);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be("revoked-kid:revoked-ec-kid");
    }

    [Fact]
    public void Verify_ReplayedOlderDocument_RejectsDueToHighWaterMark()
    {
        using var ecKey = Es256SignatureProvider.GenerateKey("prd-ec-kid");
        var signer = new LicenseDocumentSigner([ecKey]);

        using var keyRing = new SymbolonKeyRing();
        keyRing.Add(ecKey);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims = new LicenseClaims
        {
            Iss = "https://licenses.acme.example",
            Sub = "lic_01JQ8ZK4N9V2X6M0",
            Aud = "acme-cad",
            Jti = "lf_01JQ8ZK5T3P7Q1R4",
            Iat = now - 3600, // issued 1 hour ago
            Exp = now + 3600,
            Symlic = new SymlicClaims
            {
                V = 1,
                Profile = "classical-v1",
                RequiredAlgs = [Alg.Es256],
                License = new LicenseMetadata { Model = "floating", State = "active" },
                Limits = new LicenseLimits { SeatUnit = "machine" }
            }
        };

        string pem = signer.Sign(claims);

        // Verifier with HighWaterMark set to now (already saw a newer document issued just now)
        var verifier = new LicenseDocumentVerifier(keyRing, options: new SymbolonVerifierOptions
        {
            HighWaterMarkIat = now
        });

        var result = verifier.Verify(pem);
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be("replayed-older-document");
    }

    [Fact]
    public void Verify_InactiveLicenseState_Fails()
    {
        using var ecKey = Es256SignatureProvider.GenerateKey("prd-ec-kid");
        var signer = new LicenseDocumentSigner([ecKey]);

        using var keyRing = new SymbolonKeyRing();
        keyRing.Add(ecKey);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims = new LicenseClaims
        {
            Iss = "https://licenses.acme.example",
            Sub = "lic_01JQ8ZK4N9V2X6M0",
            Aud = "acme-cad",
            Jti = "lf_01JQ8ZK5T3P7Q1R4",
            Iat = now,
            Exp = now + 3600,
            Symlic = new SymlicClaims
            {
                V = 1,
                Profile = "classical-v1",
                RequiredAlgs = [Alg.Es256],
                License = new LicenseMetadata { Model = "floating", State = "suspended" },
                Limits = new LicenseLimits { SeatUnit = "machine" }
            }
        };

        string pem = signer.Sign(claims);
        var verifier = new LicenseDocumentVerifier(keyRing);

        var result = verifier.Verify(pem);
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be("license-not-active:suspended");
    }

    [Fact]
    public void Verify_BareJwsJson_WithoutPemArmor_Succeeds()
    {
        using var ecKey = Es256SignatureProvider.GenerateKey("prd-ec-kid");
        var signer = new LicenseDocumentSigner([ecKey]);

        using var keyRing = new SymbolonKeyRing();
        keyRing.Add(ecKey);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims = new LicenseClaims
        {
            Iss = "https://licenses.acme.example",
            Sub = "lic_01JQ8ZK4N9V2X6M0",
            Aud = "acme-cad",
            Jti = "lf_01JQ8ZK5T3P7Q1R4",
            Iat = now,
            Exp = now + 3600,
            Symlic = new SymlicClaims
            {
                V = 1,
                Profile = "classical-v1",
                RequiredAlgs = [Alg.Es256],
                License = new LicenseMetadata { Model = "floating", State = "active" },
                Limits = new LicenseLimits { SeatUnit = "machine" }
            }
        };

        string pem = signer.Sign(claims);
        PemArmor.TryUnwrap(pem, "SYMBOLON LICENSE", out byte[]? rawJson).Should().BeTrue();
        string bareJson = Encoding.UTF8.GetString(rawJson!);

        var verifier = new LicenseDocumentVerifier(keyRing);
        var result = verifier.Verify(bareJson);

        result.IsValid.Should().BeTrue();
        result.VerifiedAlgs.Should().Contain(Alg.Es256);
    }

    [Fact]
    public void Verify_WithValidMachineBinding_LIC33_Succeeds()
    {
        using var ecKey = Es256SignatureProvider.GenerateKey("prd-ec-kid");
        var signer = new LicenseDocumentSigner([ecKey]);

        using var keyRing = new SymbolonKeyRing();
        keyRing.Add(ecKey);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims = new LicenseClaims
        {
            Iss = "https://licenses.acme.example",
            Sub = "lic_01JQ8ZK4N9V2X6M0",
            Aud = "acme-cad",
            Jti = "lf_01JQ8ZK5T3P7Q1R4",
            Iat = now,
            Exp = now + 3600,
            Symlic = new SymlicClaims
            {
                V = 1,
                Profile = "classical-v1",
                RequiredAlgs = [Alg.Es256],
                License = new LicenseMetadata { Model = "nodelock", State = "active" },
                Limits = new LicenseLimits { SeatUnit = "machine" },
                Binding = new BindingClaim
                {
                    Matching = "match-most",
                    Components = new Dictionary<string, string>
                    {
                        ["machineId"] = "ID-12345",
                        ["cpu"] = "CPU-INTEL",
                        ["board"] = "BOARD-XYZ"
                    }
                }
            }
        };

        string pem = signer.Sign(claims);

        // Matching client components (2 out of 3 match -> majority under match-most)
        var clientComponents = new Dictionary<string, string>
        {
            ["machineId"] = "ID-12345",
            ["cpu"] = "CPU-INTEL",
            ["board"] = "BOARD-UPGRADED"
        };

        var verifier = new LicenseDocumentVerifier(keyRing, new SymbolonVerifierOptions
        {
            ClientFingerprintComponents = clientComponents
        });

        var result = verifier.Verify(pem);
        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Verify_WithMismatchedMachineBinding_LIC33_FailsWithBindingMismatch()
    {
        using var ecKey = Es256SignatureProvider.GenerateKey("prd-ec-kid");
        var signer = new LicenseDocumentSigner([ecKey]);

        using var keyRing = new SymbolonKeyRing();
        keyRing.Add(ecKey);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims = new LicenseClaims
        {
            Iss = "https://licenses.acme.example",
            Sub = "lic_01JQ8ZK4N9V2X6M0",
            Aud = "acme-cad",
            Jti = "lf_01JQ8ZK5T3P7Q1R4",
            Iat = now,
            Exp = now + 3600,
            Symlic = new SymlicClaims
            {
                V = 1,
                Profile = "classical-v1",
                RequiredAlgs = [Alg.Es256],
                License = new LicenseMetadata { Model = "nodelock", State = "active" },
                Limits = new LicenseLimits { SeatUnit = "machine" },
                Binding = new BindingClaim
                {
                    Matching = "match-all",
                    Components = new Dictionary<string, string>
                    {
                        ["machineId"] = "ID-12345",
                        ["cpu"] = "CPU-INTEL"
                    }
                }
            }
        };

        string pem = signer.Sign(claims);

        // Mismatched client components
        var clientComponents = new Dictionary<string, string>
        {
            ["machineId"] = "ID-DIFFERENT",
            ["cpu"] = "CPU-INTEL"
        };

        var verifier = new LicenseDocumentVerifier(keyRing, new SymbolonVerifierOptions
        {
            ClientFingerprintComponents = clientComponents
        });

        var result = verifier.Verify(pem);
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().StartWith("binding-mismatch");
    }
}
