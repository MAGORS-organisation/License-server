using FluentAssertions;
using Symbolon.Crypto;
using Xunit;

namespace Symbolon.Protocol.Tests;

public sealed class LeaseTokenTests
{
    private static LeaseClaims CreateSampleLeaseClaims(long now, string fpHash = "sha256:abc123", long seq = 1)
    {
        return new LeaseClaims
        {
            Iss = "relay:rly_01JQ",
            Sub = "lic_01JQ8ZK4N9V2X6M0",
            Jti = "lse_01JQ9A",
            Iat = now,
            Exp = now + 600, // 10 min
            Seat = 3,
            Fp = fpHash,
            Ent = ["core", "module.cad-export"],
            Gnt = "gnt_01JQ",
            Seq = seq
        };
    }

    [Fact]
    public void LeaseToken_IssueAndVerify_Succeeds()
    {
        using var key = Es256SignatureProvider.GenerateKey("lease-key-1");
        var signer = new LeaseTokenSigner(key);

        using var keyRing = new SymbolonKeyRing();
        keyRing.Add(key);
        var verifier = new LeaseTokenVerifier(keyRing);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims = CreateSampleLeaseClaims(now);

        string token = signer.IssueToken(claims);

        var result = verifier.Verify(
            token: token,
            expectedFpHash: "sha256:abc123",
            expectedLicenseId: "lic_01JQ8ZK4N9V2X6M0",
            lastSeenSeq: 0);

        result.IsValid.Should().BeTrue();
        result.FailureReason.Should().BeNull();
        result.Claims!.Seat.Should().Be(3);
        result.Claims.Seq.Should().Be(1);
    }

    [Fact]
    public void LeaseToken_Verify_FingerprintMismatch_Rejects()
    {
        using var key = Es256SignatureProvider.GenerateKey("lease-key-1");
        var signer = new LeaseTokenSigner(key);

        using var keyRing = new SymbolonKeyRing();
        keyRing.Add(key);
        var verifier = new LeaseTokenVerifier(keyRing);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims = CreateSampleLeaseClaims(now, fpHash: "sha256:correct");

        string token = signer.IssueToken(claims);

        var result = verifier.Verify(
            token: token,
            expectedFpHash: "sha256:different");

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be("fingerprint-mismatch");
    }

    [Fact]
    public void LeaseToken_Verify_StaleSequence_Rejects()
    {
        using var key = Es256SignatureProvider.GenerateKey("lease-key-1");
        var signer = new LeaseTokenSigner(key);

        using var keyRing = new SymbolonKeyRing();
        keyRing.Add(key);
        var verifier = new LeaseTokenVerifier(keyRing);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims = CreateSampleLeaseClaims(now, seq: 5);

        string token = signer.IssueToken(claims);

        // Verifier already saw sequence 5 or higher
        var result = verifier.Verify(
            token: token,
            lastSeenSeq: 5);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be("stale-sequence");
    }

    [Fact]
    public void LeaseToken_Verify_ExpiredToken_Rejects()
    {
        using var key = Es256SignatureProvider.GenerateKey("lease-key-1");
        var signer = new LeaseTokenSigner(key);

        using var keyRing = new SymbolonKeyRing();
        keyRing.Add(key);
        var verifier = new LeaseTokenVerifier(keyRing);

        long past = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - 1000;
        var claims = new LeaseClaims
        {
            Iss = "relay:rly_01JQ",
            Sub = "lic_01JQ8ZK4N9V2X6M0",
            Jti = "lse_01JQ9A",
            Iat = past - 600,
            Exp = past, // expired 1000s ago
            Seat = 1,
            Fp = "sha256:abc",
            Ent = ["core"],
            Seq = 1
        };

        string token = signer.IssueToken(claims);

        var result = verifier.Verify(token);
        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be("expired");
    }
}
