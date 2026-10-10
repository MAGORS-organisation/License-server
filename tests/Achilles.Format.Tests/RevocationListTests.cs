using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Achilles.Crypto;
using Xunit;

namespace Achilles.Format.Tests;

public sealed class RevocationListTests
{
    private static RevocationListClaims CreateSampleRevocationClaims(
        long nowSeconds,
        long seq = 1,
        bool full = true,
        long? since = null,
        IReadOnlyList<RevocationItem>? items = null)
    {
        return new RevocationListClaims(
            Iss: "https://licenses.acme.example",
            Iat: nowSeconds,
            Exp: nowSeconds + 24 * 3600,
            Symrl: new RevocationPayload(
                V: 1,
                Seq: seq,
                Full: full,
                Since: since,
                Revoked: items ??
                [
                    new RevocationItem(RevocationItem.TypeLicense, "lic_01JQ8ZK4N9V2X6M0", nowSeconds - 3600, "non-payment"),
                    new RevocationItem(RevocationItem.TypeMachine, "mch_01JQ8ZK4N9V2X6M1", nowSeconds - 1800, "hardware-cloned"),
                    new RevocationItem(RevocationItem.TypeKid, "prd-acme-2025-ec", nowSeconds - 7200, "key-compromise"),
                    new RevocationItem(RevocationItem.TypeRelay, "rel_01JQ8ZK4N9V2X6M2", nowSeconds - 600, "rogue-relay"),
                    new RevocationItem("unknown_device_type", "dev_01JQ8ZK4N9V2X6M3", nowSeconds - 300, "ignored-per-rvl6")
                ]
            )
        );
    }

    [Fact]
    public void RevocationList_SignAndVerify_Roundtrip_Succeeds()
    {
        using var ecKey = Es256SignatureProvider.GenerateKey("prd-acme-2026-09-ec");
        using var pqKey = MlDsaSignatureProvider.GenerateKey(MLDsaAlgorithm.MLDsa65, "prd-acme-2026-09-pq");

        var signer = new RevocationListSigner([ecKey, pqKey]);

        using var keyRing = new AchillesKeyRing();
        keyRing.Add(ecKey);
        keyRing.Add(pqKey);

        var verifier = new RevocationListVerifier(keyRing);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims = CreateSampleRevocationClaims(now, seq: 10, full: true);

        string pem = signer.Sign(claims);
        pem.Should().Contain("-----BEGIN SYMBOLON REVOCATION LIST-----");
        pem.Should().Contain("-----END SYMBOLON REVOCATION LIST-----");

        var result = verifier.Verify(pem);

        result.IsValid.Should().BeTrue();
        result.FailureReason.Should().BeNull();
        result.VerifiedAlgs.Should().Contain([Alg.Es256, Alg.MlDsa65]);
        result.Claims!.Symrl.Seq.Should().Be(10);
        result.Claims.Symrl.Full.Should().BeTrue();
        result.Claims.Symrl.Revoked.Should().HaveCount(5);
    }

    [Fact]
    public void RevocationList_DeltaChaining_Succeeds()
    {
        using var ecKey = Es256SignatureProvider.GenerateKey("prd-acme-2026-09-ec");
        var signer = new RevocationListSigner([ecKey]);

        using var keyRing = new AchillesKeyRing();
        keyRing.Add(ecKey);

        var verifier = new RevocationListVerifier(keyRing, options: new RevocationVerifierOptions
        {
            LastSeenSeq = 100
        });

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        // Delta from 100 to 105
        var claims = CreateSampleRevocationClaims(now, seq: 105, full: false, since: 100);

        string pem = signer.Sign(claims);
        var result = verifier.Verify(pem);

        result.IsValid.Should().BeTrue();
        result.Claims!.Symrl.Full.Should().BeFalse();
        result.Claims.Symrl.Since.Should().Be(100);
        result.Claims.Symrl.Seq.Should().Be(105);
    }

    [Fact]
    public void RevocationList_DeltaGap_Rejects()
    {
        using var ecKey = Es256SignatureProvider.GenerateKey("prd-acme-2026-09-ec");
        var signer = new RevocationListSigner([ecKey]);

        using var keyRing = new AchillesKeyRing();
        keyRing.Add(ecKey);

        var verifier = new RevocationListVerifier(keyRing, options: new RevocationVerifierOptions
        {
            LastSeenSeq = 100
        });

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        // Client last saw 100, but delta starts at 102 (gap: missed 101)
        var claims = CreateSampleRevocationClaims(now, seq: 105, full: false, since: 102);

        string pem = signer.Sign(claims);
        var result = verifier.Verify(pem);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().StartWith("delta-gap");
    }

    [Fact]
    public void RevocationList_StaleSequence_Rejects()
    {
        using var ecKey = Es256SignatureProvider.GenerateKey("prd-acme-2026-09-ec");
        var signer = new RevocationListSigner([ecKey]);

        using var keyRing = new AchillesKeyRing();
        keyRing.Add(ecKey);

        var verifier = new RevocationListVerifier(keyRing, options: new RevocationVerifierOptions
        {
            LastSeenSeq = 100
        });

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        // Sequence 95 is older than last seen 100
        var claims = CreateSampleRevocationClaims(now, seq: 95, full: true);

        string pem = signer.Sign(claims);
        var result = verifier.Verify(pem);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().StartWith("stale-sequence");
    }

    [Fact]
    public void RevocationList_TamperedPayload_RejectsWithBadSignature()
    {
        using var ecKey = Es256SignatureProvider.GenerateKey("prd-acme-2026-09-ec");
        var signer = new RevocationListSigner([ecKey]);

        using var keyRing = new AchillesKeyRing();
        keyRing.Add(ecKey);

        var verifier = new RevocationListVerifier(keyRing);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims = CreateSampleRevocationClaims(now, seq: 5);

        string pem = signer.Sign(claims);
        PemArmor.TryUnwrap(pem, RevocationListSigner.DefaultPemLabel, out byte[]? rawJson).Should().BeTrue();
        var jwsDoc = JsonSerializer.Deserialize(rawJson!, AchillesJsonContext.Default.JwsGeneralJson)!;

        // Corrupt the signature bytes
        char flipped = jwsDoc.Signatures[0].Signature[0] == 'A' ? 'B' : 'A';
        var corruptedSig = new JwsSignature(jwsDoc.Signatures[0].Protected, flipped + jwsDoc.Signatures[0].Signature[1..]);
        var corruptedDoc = new JwsGeneralJson(jwsDoc.Payload, [corruptedSig]);
        string tampered = PemArmor.Wrap(RevocationListSigner.DefaultPemLabel, JsonSerializer.SerializeToUtf8Bytes(corruptedDoc, AchillesJsonContext.Default.JwsGeneralJson));

        var result = verifier.Verify(tampered);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().StartWith("bad-signature");
    }

    [Fact]
    public void RevocationList_RevokedSigningKey_RejectsPerRVL14()
    {
        using var ecKey = Es256SignatureProvider.GenerateKey("compromised-key-2026");
        var signer = new RevocationListSigner([ecKey]);

        using var keyRing = new AchillesKeyRing();
        keyRing.Add(ecKey);
        keyRing.Revoke("compromised-key-2026"); // Explicitly revoke the key

        var verifier = new RevocationListVerifier(keyRing);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims = CreateSampleRevocationClaims(now, seq: 1);

        string pem = signer.Sign(claims);
        var result = verifier.Verify(pem);

        result.IsValid.Should().BeFalse();
        result.FailureReason.Should().Be("revoked-signing-kid:compromised-key-2026");
    }

    [Fact]
    public void RevocationList_UnknownSubjectType_IgnoredPerRVL6()
    {
        using var ecKey = Es256SignatureProvider.GenerateKey("prd-acme-2026-09-ec");
        var signer = new RevocationListSigner([ecKey]);

        using var keyRing = new AchillesKeyRing();
        keyRing.Add(ecKey);

        var verifier = new RevocationListVerifier(keyRing);

        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var claims = CreateSampleRevocationClaims(now, items:
        [
            new RevocationItem("future_hardware_token", "hw_token_99", now - 100, "quantum-module")
        ]);

        string pem = signer.Sign(claims);
        var result = verifier.Verify(pem);

        result.IsValid.Should().BeTrue();
        result.Claims!.Symrl.Revoked.Should().ContainSingle();
        result.Claims.Symrl.Revoked[0].T.Should().Be("future_hardware_token");
        result.Claims.Symrl.Revoked[0].IsKnownType.Should().BeFalse();
    }
}
