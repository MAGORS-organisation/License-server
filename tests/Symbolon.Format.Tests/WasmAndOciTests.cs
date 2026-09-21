using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using FluentAssertions;
using Symbolon.Crypto;
using Symbolon.Format.Oci;
using Symbolon.Format.Wasm;
using Xunit;

namespace Symbolon.Format.Tests;

public sealed class WasmAndOciTests
{
    private static (string Pem, string JwksJson, Es256SignatureProvider Key) GenerateSampleLicenseAndJwks(string? fingerprint = null, long? expOffsetSeconds = null)
    {
        var key = Es256SignatureProvider.GenerateKey("key-wasm-test");
        var signer = new LicenseDocumentSigner([key]);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        long exp = now + (expOffsetSeconds ?? (30 * 86400));

        var claims = new LicenseClaims
        {
            Iss = "symbolon:control-plane",
            Sub = "lic_wasm_123",
            Aud = "enterprise-client",
            Jti = "jti_123",
            Iat = now,
            Exp = exp,
            Symlic = new SymlicClaims
            {
                V = 1,
                Profile = "hybrid-v1",
                RequiredAlgs = [Alg.Es256],
                License = new LicenseMetadata
                {
                    Key = "SYM-WASM-TEST-1234-5678",
                    Model = fingerprint is not null ? "nodelock" : "floating",
                    State = "active",
                    Customer = new CustomerMetadata
                    {
                        Name = "ACME International",
                        Ref = "CUST-001"
                    }
                },
                Limits = new LicenseLimits
                {
                    MaxSeats = 5,
                    SeatUnit = "machine"
                },
                Entitlements =
                [
                    new EntitlementClaim { Code = "core" },
                    new EntitlementClaim { Code = "export" }
                ],
                Binding = fingerprint is not null ? new BindingClaim { Fingerprint = fingerprint } : null
            }
        };

        string pem = signer.Sign(claims);
        var jwk = key.ExportPublicJwk();
        var jwks = new { keys = new[] { jwk } };
        string jwksJson = JsonSerializer.Serialize(jwks);

        return (pem, jwksJson, key);
    }

    [Fact]
    public void WasmLicenseValidator_Validates_Genuine_License_Offline()
    {
        var (pem, jwksJson, key) = GenerateSampleLicenseAndJwks();
        using (key)
        {
            var result = WasmLicenseValidator.Validate(pem, jwksJson, expectedAudience: "enterprise-client");

            result.IsValid.Should().BeTrue();
            result.FailureReason.Should().BeNull();
            result.Customer.Should().Be("ACME International");
            result.Product.Should().Be("enterprise-client");
            result.MaxSeats.Should().Be(5);
            result.DaysRemaining.Should().BeGreaterThan(0);
            result.IsExpired.Should().BeFalse();
        }
    }

    [Fact]
    public void WasmLicenseValidator_Rejects_NodeLock_Fingerprint_Mismatch()
    {
        var (pem, jwksJson, key) = GenerateSampleLicenseAndJwks(fingerprint: "hw_fingerprint_server_1");
        using (key)
        {
            // Validating on a machine with a different fingerprint
            var result = WasmLicenseValidator.Validate(pem, jwksJson, fingerprint: "hw_fingerprint_rogue_station");

            result.IsValid.Should().BeFalse();
            result.MachineMatch.Should().BeFalse();
            result.FailureReason.Should().Contain("fingerprint mismatch");
        }
    }

    [Fact]
    public void WasmLicenseValidator_Rejects_Expired_License()
    {
        // Expired 10 days ago
        var (pem, jwksJson, key) = GenerateSampleLicenseAndJwks(expOffsetSeconds: -10 * 86400);
        using (key)
        {
            var result = WasmLicenseValidator.Validate(pem, jwksJson);

            result.IsValid.Should().BeFalse();
            result.IsExpired.Should().BeTrue();
            result.FailureReason.Should().Contain("expired");
        }
    }

    [Fact]
    public void OciLicenseBundle_Packs_And_Verifies_Tar_Archive()
    {
        var (pem, jwksJson, key) = GenerateSampleLicenseAndJwks();
        using (key)
        {
            // 1. Pack OCI artifact bundle
            byte[] ociBundle = OciLicenseBundle.CreateBundle(pem, jwksJson, "ACME International", "EnterpriseCAD");
            ociBundle.Should().NotBeNull();
            ociBundle.Length.Should().BeGreaterThan(512);

            // 2. Unpack and verify OCI bundle integrity
            var verification = OciLicenseBundle.VerifyBundle(ociBundle);

            verification.IsValid.Should().BeTrue();
            verification.FailureReason.Should().BeNull();
            verification.LicensePayload.Should().NotBeNullOrWhiteSpace();
            verification.JwksPayload.Should().NotBeNullOrWhiteSpace();
            verification.LicenseDetails.Should().NotBeNull();
            verification.LicenseDetails!.Customer.Should().Be("ACME International");
        }
    }

    [Fact]
    public void OciLicenseBundle_Rejects_Tampered_Blob()
    {
        var (pem, jwksJson, key) = GenerateSampleLicenseAndJwks();
        using (key)
        {
            byte[] ociBundle = OciLicenseBundle.CreateBundle(pem, jwksJson, "ACME", "CAD");

            // Tamper with bytes inside the archive
            for (int i = 500; i < 550 && i < ociBundle.Length; i++)
            {
                ociBundle[i] ^= 0xFF;
            }

            var verification = OciLicenseBundle.VerifyBundle(ociBundle);
            verification.IsValid.Should().BeFalse();
        }
    }
}
