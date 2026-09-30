using System.Security.Cryptography;
using FluentAssertions;
using Symbolon.Crypto;
using Xunit;

namespace Symbolon.Format.Tests;

public sealed class SeatGrantTests
{
    private static SeatGrantDocumentClaims CreateSampleSeatGrantClaims(
        long nowSeconds,
        string licenseId = "lic_01JQ8ZK4N9V2X6M0",
        string relayId = "rly_01JQ8ZM2ACME001",
        int seats = 10,
        int seatFrom = 0,
        int seatTo = 9,
        long seq = 42,
        long? supersedes = 41)
    {
        long exp = nowSeconds + 86400; // 24h
        return new SeatGrantDocumentClaims
        {
            Iss = "https://licenses.acme.example",
            Sub = licenseId,
            Aud = relayId,
            Jti = "gnt_01JQ8ZN7TEST001",
            Iat = nowSeconds,
            Nbf = nowSeconds,
            Exp = exp,
            Symgrant = new SeatGrantPayload
            {
                V = 1,
                Seats = seats,
                SeatRange = [seatFrom, seatTo],
                Seq = seq,
                Supersedes = supersedes,
                Entitlements = ["core", "module.cad-export"],
                LeasePolicy = new LeasePolicyClaim { Ttl = "PT10M", GraceTtl = "PT4H" },
                LeaseKey = new JsonWebKeyDto
                {
                    Kty = "EC",
                    Alg = Alg.Es256,
                    Crv = "P-256",
                    X = "test-x",
                    Y = "test-y",
                    Kid = "lease-rly1-2026-09",
                    D = "test-private-d"
                },
                OfflineExtension = new OfflineExtensionClaim { Allowed = true, MaxExtensions = 7 }
            }
        };
    }

    private static AirGapRequestClaims CreateSampleAirGapRequestClaims(
        long nowSeconds,
        string licenseId = "lic_01JQ8ZK4N9V2X6M0",
        string relayId = "rly_01JQ8ZM2ACME001",
        int requestedSeats = 5,
        long lastSeq = 41,
        string usageDigest = "sha256:4a6f2389...",
        string nonce = "nonce_01JQ8ZPTEST")
    {
        return new AirGapRequestClaims
        {
            Iss = relayId,
            Sub = "SYM1-DEMO-ACME-TEST-KEY1",
            Jti = "req_01JQ8ZR001",
            Iat = nowSeconds,
            Exp = nowSeconds + 3600,
            Symreq = new AirGapRequestPayload
            {
                V = 1,
                RelayId = relayId,
                LicenseKey = "SYM1-DEMO-ACME-TEST-KEY1",
                RequestedSeats = requestedSeats,
                LastSeq = lastSeq,
                UsageDigest = usageDigest,
                Nonce = nonce
            }
        };
    }

    [Fact]
    public void SeatGrant_SignAndVerify_Hybrid_Roundtrip_Succeeds()
    {
        using var ecKey = Es256SignatureProvider.GenerateKey("prd-acme-2026-09-ec");
        using var pqKey = MlDsaSignatureProvider.GenerateKey(MLDsaAlgorithm.MLDsa65, "prd-acme-2026-09-pq");

        var signer = new SeatGrantSigner([ecKey, pqKey]);

        using var keyRing = new SymbolonKeyRing();
        keyRing.Add(ecKey);
        keyRing.Add(pqKey);

        var verifier = new SeatGrantVerifier(keyRing, options: new SeatGrantVerifierOptions
        {
            Strictness = Strictness.Strict,
            ExpectedRelayId = "rly_01JQ8ZM2ACME001",
            ExpectedLicenseId = "lic_01JQ8ZK4N9V2X6M0",
            MinSeq = 41
        });

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims = CreateSampleSeatGrantClaims(now);

        string pem = signer.Sign(claims);
        pem.Should().Contain("-----BEGIN SYMBOLON SEAT GRANT-----");
        pem.Should().Contain("-----END SYMBOLON SEAT GRANT-----");

        var result = verifier.Verify(pem);
        result.IsValid.Should().BeTrue();
        result.Claims.Should().NotBeNull();
        result.Claims!.Sub.Should().Be("lic_01JQ8ZK4N9V2X6M0");
        result.Claims.Aud.Should().Be("rly_01JQ8ZM2ACME001");
        result.Claims.Symgrant.Seats.Should().Be(10);
        result.Claims.Symgrant.SeatRange.Should().Equal(0, 9);
        result.Claims.Symgrant.Seq.Should().Be(42);
        result.Claims.Symgrant.Supersedes.Should().Be(41);
        result.VerifiedAlgs.Should().Contain(Alg.Es256);
        result.VerifiedAlgs.Should().Contain(Alg.MlDsa65);
    }

    [Fact]
    public void SeatGrant_GNT3_SeatRangeMismatch_ThrowsOnSign()
    {
        using var ecKey = Es256SignatureProvider.GenerateKey("prd-acme-2026-09-ec");
        var signer = new SeatGrantSigner([ecKey]);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        // 10 seats declared, but range [0, 8] is only 9 seats!
        var invalidClaims = CreateSampleSeatGrantClaims(now, seats: 10, seatFrom: 0, seatTo: 8);

        Action act = () => signer.Sign(invalidClaims);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*GNT-3*");
    }

    [Fact]
    public void SeatGrant_GNT7_RollbackSequence_FailsVerification()
    {
        using var ecKey = Es256SignatureProvider.GenerateKey("prd-acme-2026-09-ec");
        var signer = new SeatGrantSigner([ecKey]);

        using var keyRing = new SymbolonKeyRing();
        keyRing.Add(ecKey);

        // Relay has already seen seq 42, requires minSeq = 43
        var verifier = new SeatGrantVerifier(keyRing, options: new SeatGrantVerifierOptions
        {
            MinSeq = 43
        });

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims = CreateSampleSeatGrantClaims(now, seq: 42);
        string pem = signer.Sign(claims);

        var result = verifier.Verify(pem);
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Contain("sequence-rollback-detected");
    }

    [Fact]
    public void SeatGrant_Expired_FailsVerification()
    {
        using var ecKey = Es256SignatureProvider.GenerateKey("prd-acme-2026-09-ec");
        var signer = new SeatGrantSigner([ecKey]);

        using var keyRing = new SymbolonKeyRing();
        keyRing.Add(ecKey);

        var verifier = new SeatGrantVerifier(keyRing, options: new SeatGrantVerifierOptions
        {
            ClockSkewTolerance = TimeSpan.Zero
        });

        long past = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 100000;
        var claims = CreateSampleSeatGrantClaims(past);
        string pem = signer.Sign(claims);

        var result = verifier.Verify(pem);
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Contain("grant-expired");
    }

    [Fact]
    public void SeatGrant_AudienceMismatch_FailsVerification()
    {
        using var ecKey = Es256SignatureProvider.GenerateKey("prd-acme-2026-09-ec");
        var signer = new SeatGrantSigner([ecKey]);

        using var keyRing = new SymbolonKeyRing();
        keyRing.Add(ecKey);

        var verifier = new SeatGrantVerifier(keyRing, options: new SeatGrantVerifierOptions
        {
            ExpectedRelayId = "rly_DIFFERENT_RELAY"
        });

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims = CreateSampleSeatGrantClaims(now, relayId: "rly_01JQ8ZM2ACME001");
        string pem = signer.Sign(claims);

        var result = verifier.Verify(pem);
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Contain("audience-mismatch");
    }

    [Fact]
    public void SeatGrant_RevokedKey_FailsVerification()
    {
        using var ecKey = Es256SignatureProvider.GenerateKey("prd-acme-2026-09-ec");
        var signer = new SeatGrantSigner([ecKey]);

        using var keyRing = new SymbolonKeyRing();
        keyRing.Add(ecKey);
        keyRing.Revoke("prd-acme-2026-09-ec");

        var verifier = new SeatGrantVerifier(keyRing);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims = CreateSampleSeatGrantClaims(now);
        string pem = signer.Sign(claims);

        var result = verifier.Verify(pem);
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Contain("revoked-kid");
    }

    [Fact]
    public void AirGapRequest_SignAndVerify_Roundtrip_Succeeds()
    {
        using var relayKey = Es256SignatureProvider.GenerateKey("rly-key-01");
        var signer = new AirGapRequestSigner(relayKey);
        var verifier = new AirGapRequestVerifier(relayKey);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims = CreateSampleAirGapRequestClaims(now);

        string pem = signer.Sign(claims);
        pem.Should().Contain("-----BEGIN SYMBOLON GRANT REQUEST-----");
        pem.Should().Contain("-----END SYMBOLON GRANT REQUEST-----");

        var result = verifier.Verify(pem);
        result.IsValid.Should().BeTrue();
        result.Claims.Should().NotBeNull();
        result.Claims!.Iss.Should().Be("rly_01JQ8ZM2ACME001");
        result.Claims.Symreq.LicenseKey.Should().Be("SYM1-DEMO-ACME-TEST-KEY1");
        result.Claims.Symreq.RequestedSeats.Should().Be(5);
        result.Claims.Symreq.LastSeq.Should().Be(41);
        result.Claims.Symreq.UsageDigest.Should().Be("sha256:4a6f2389...");
        result.Claims.Symreq.Nonce.Should().Be("nonce_01JQ8ZPTEST");
    }

    [Fact]
    public void AirGapRequest_Expired_FailsVerification()
    {
        using var relayKey = Es256SignatureProvider.GenerateKey("rly-key-01");
        var signer = new AirGapRequestSigner(relayKey);
        var verifier = new AirGapRequestVerifier(relayKey);

        long past = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 7200;
        var claims = CreateSampleAirGapRequestClaims(past);
        string pem = signer.Sign(claims);

        var result = verifier.Verify(pem);
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Contain("request-expired");
    }

    [Fact]
    public void AirGapRequest_Tampered_FailsVerification()
    {
        using var relayKey = Es256SignatureProvider.GenerateKey("rly-key-01");
        var signer = new AirGapRequestSigner(relayKey);
        var verifier = new AirGapRequestVerifier(relayKey);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims = CreateSampleAirGapRequestClaims(now);
        string pem = signer.Sign(claims);

        // Tamper with PEM body
        string[] lines = pem.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        lines[1] = (lines[1][0] == 'A' ? 'B' : 'A') + lines[1][1..];
        string tampered = string.Join("\n", lines);

        var result = verifier.Verify(tampered);
        result.IsValid.Should().BeFalse();
    }
}
