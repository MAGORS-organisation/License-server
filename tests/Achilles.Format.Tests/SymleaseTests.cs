using System.Security.Cryptography;
using FluentAssertions;
using Achilles.Crypto;
using Xunit;

namespace Achilles.Format.Tests;

public sealed class SymleaseTests
{
    private static SymleaseClaims CreateSampleSymleaseClaims(
        long nowSeconds,
        string licenseId = "lic_01JQ8ZK4N9V2X6M0",
        string fpHash = "sha256:e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
        int days = 7)
    {
        long exp = nowSeconds + days * 86400;
        return new SymleaseClaims(
            Iss: "https://licenses.acme.example",
            Sub: licenseId,
            Jti: "lse_01JQ9A7B3X9V2Z",
            Iat: nowSeconds,
            Nbf: nowSeconds - 60,
            Exp: exp,
            Seat: 4,
            Fp: fpHash,
            Ent: ["cad-core", "fea-solver"],
            Borrow: new SymleaseBorrowPayload(
                Days: days,
                BorrowedAt: nowSeconds,
                BorrowedUntil: exp,
                PossessionKeyJwk: "{\"kty\":\"EC\",\"crv\":\"P-256\",\"x\":\"sampleX\",\"y\":\"sampleY\"}"
            )
        );
    }

    [Fact]
    public void Symlease_SignAndVerify_Roundtrip_Succeeds()
    {
        using var ecKey = Es256SignatureProvider.GenerateKey("prd-acme-2026-09-ec");
        using var pqKey = MlDsaSignatureProvider.GenerateKey(MLDsaAlgorithm.MLDsa65, "prd-acme-2026-09-pq");

        var signer = new SymleaseSigner([ecKey, pqKey]);

        using var keyRing = new AchillesKeyRing();
        keyRing.Add(ecKey);
        keyRing.Add(pqKey);

        var verifier = new SymleaseVerifier(keyRing);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims = CreateSampleSymleaseClaims(now);

        string pem = signer.Sign(claims);
        pem.Should().Contain("-----BEGIN SYMBOLON LEASE-----");
        pem.Should().Contain("-----END SYMBOLON LEASE-----");

        var result = verifier.Verify(pem);
        result.IsValid.Should().BeTrue();
        result.Claims.Should().NotBeNull();
        result.Claims!.Sub.Should().Be("lic_01JQ8ZK4N9V2X6M0");
        result.Claims.Seat.Should().Be(4);
        result.Claims.Borrow.Days.Should().Be(7);
        result.VerifiedAlgs.Should().Contain("ES256");
        result.VerifiedAlgs.Should().Contain("ML-DSA-65");
    }

    [Fact]
    public void Symlease_Expired_FailsVerification()
    {
        using var ecKey = Es256SignatureProvider.GenerateKey("prd-acme-2026-09-ec");
        var signer = new SymleaseSigner([ecKey]);

        using var keyRing = new AchillesKeyRing();
        keyRing.Add(ecKey);

        var verifier = new SymleaseVerifier(keyRing, options: new SymleaseVerifierOptions
        {
            ClockSkewTolerance = TimeSpan.Zero
        });

        long past = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 100000;
        var claims = new SymleaseClaims(
            Iss: "https://licenses.acme.example",
            Sub: "lic_01JQ8ZK4N9V2X6M0",
            Jti: "lse_expired",
            Iat: past - 86400,
            Nbf: past - 86400,
            Exp: past,
            Seat: 1,
            Fp: "sha256:abc",
            Ent: [],
            Borrow: new SymleaseBorrowPayload(1, past - 86400, past, "{}")
        );

        string pem = signer.Sign(claims);
        var result = verifier.Verify(pem);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be("symlease-expired");
    }

    [Fact]
    public void Symlease_LicenseIdMismatch_FailsVerification()
    {
        using var ecKey = Es256SignatureProvider.GenerateKey("prd-acme-2026-09-ec");
        var signer = new SymleaseSigner([ecKey]);

        using var keyRing = new AchillesKeyRing();
        keyRing.Add(ecKey);

        var verifier = new SymleaseVerifier(keyRing, options: new SymleaseVerifierOptions
        {
            ExpectedLicenseId = "lic_EXPECTED_OTHER"
        });

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims = CreateSampleSymleaseClaims(now, licenseId: "lic_ACTUAL");

        string pem = signer.Sign(claims);
        var result = verifier.Verify(pem);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be("license-id-mismatch");
    }

    [Fact]
    public void Symlease_FingerprintMismatch_FailsVerification()
    {
        using var ecKey = Es256SignatureProvider.GenerateKey("prd-acme-2026-09-ec");
        var signer = new SymleaseSigner([ecKey]);

        using var keyRing = new AchillesKeyRing();
        keyRing.Add(ecKey);

        var verifier = new SymleaseVerifier(keyRing, options: new SymleaseVerifierOptions
        {
            ExpectedFingerprint = "sha256:EXPECTED_FP"
        });

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims = CreateSampleSymleaseClaims(now, fpHash: "sha256:DIFFERENT_FP");

        string pem = signer.Sign(claims);
        var result = verifier.Verify(pem);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be("fingerprint-mismatch");
    }

    [Fact]
    public void Symlease_RevokedKid_FailsVerification()
    {
        using var ecKey = Es256SignatureProvider.GenerateKey("prd-revoked-kid");
        var signer = new SymleaseSigner([ecKey]);

        using var keyRing = new AchillesKeyRing();
        keyRing.Add(ecKey);
        keyRing.Revoke("prd-revoked-kid");

        var verifier = new SymleaseVerifier(keyRing);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims = CreateSampleSymleaseClaims(now);

        string pem = signer.Sign(claims);
        var result = verifier.Verify(pem);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be("no-valid-signature");
    }
}
