using System.Security.Cryptography;
using FluentAssertions;
using Achilles.Crypto.Kms;
using Xunit;

namespace Achilles.Crypto.Tests;

public sealed class EncryptedEnvelopeKeyStoreTests
{
    private const string TestPassphrase = "SuperSecretMasterPassphrase_2026!#";

    [Fact]
    public void EncryptAndDecrypt_RoundTrip_RestoresOriginalKeyBytes()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] originalPkcs8 = ecdsa.ExportPkcs8PrivateKey();

        var envelope = EncryptedEnvelopeKeyStore.Encrypt(
            pkcs8Bytes: originalPkcs8,
            passphrase: TestPassphrase,
            kid: "key-envelope-test",
            alg: "ES256",
            kty: "EC");

        envelope.Kid.Should().Be("key-envelope-test");
        envelope.Alg.Should().Be("ES256");
        envelope.CipherText.Should().NotBeNullOrWhiteSpace();

        byte[] decrypted = EncryptedEnvelopeKeyStore.Decrypt(envelope, TestPassphrase);
        decrypted.Should().Equal(originalPkcs8);
    }

    [Fact]
    public void Decrypt_WithWrongPassphrase_ThrowsCryptographicException()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] originalPkcs8 = ecdsa.ExportPkcs8PrivateKey();

        var envelope = EncryptedEnvelopeKeyStore.Encrypt(
            pkcs8Bytes: originalPkcs8,
            passphrase: TestPassphrase,
            kid: "key-wrong-pass-test",
            alg: "ES256",
            kty: "EC");

        var act = () => EncryptedEnvelopeKeyStore.Decrypt(envelope, "IncorrectWrongPassword123!");
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void Decrypt_WithTamperedCipherText_ThrowsCryptographicException()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] originalPkcs8 = ecdsa.ExportPkcs8PrivateKey();

        var envelope = EncryptedEnvelopeKeyStore.Encrypt(
            pkcs8Bytes: originalPkcs8,
            passphrase: TestPassphrase,
            kid: "key-tamper-test",
            alg: "ES256",
            kty: "EC");

        byte[] rawCipher = Convert.FromBase64String(envelope.CipherText);
        rawCipher[0] ^= 0xff; // Invert first byte

        var tamperedEnvelope = envelope with { CipherText = Convert.ToBase64String(rawCipher) };

        var act = () => EncryptedEnvelopeKeyStore.Decrypt(tamperedEnvelope, TestPassphrase);
        act.Should().Throw<CryptographicException>();
    }

    [Fact]
    public void PemArmor_SerializeAndDeserialize_PreservesEnvelope()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] originalPkcs8 = ecdsa.ExportPkcs8PrivateKey();

        var envelope = EncryptedEnvelopeKeyStore.Encrypt(
            pkcs8Bytes: originalPkcs8,
            passphrase: TestPassphrase,
            kid: "pem-roundtrip-key",
            alg: "ES256",
            kty: "EC");

        string pem = EncryptedEnvelopeKeyStore.ToPem(envelope);
        pem.Should().Contain("-----BEGIN ENCRYPTED SYMBOLON KEY-----");
        pem.Should().Contain("-----END ENCRYPTED SYMBOLON KEY-----");

        var restored = EncryptedEnvelopeKeyStore.FromPem(pem);
        restored.Kid.Should().Be("pem-roundtrip-key");
        restored.Alg.Should().Be("ES256");

        byte[] decrypted = EncryptedEnvelopeKeyStore.Decrypt(restored, TestPassphrase);
        decrypted.Should().Equal(originalPkcs8);
    }

    [Fact]
    public async Task EncryptedEnvelopeKmsProvider_SignsAndVerifies_Successfully()
    {
        using var provider = new EncryptedEnvelopeKmsProvider(TestPassphrase);

        using var testKey = Es256SignatureProvider.GenerateKey("kms-envelope-key-1");
        byte[] pkcs8Bytes = testKey.ExportPrivateKeyBytes();
        var jwk = testKey.ExportPublicJwk();

        var envelope = EncryptedEnvelopeKeyStore.Encrypt(
            pkcs8Bytes: pkcs8Bytes,
            passphrase: TestPassphrase,
            kid: testKey.Kid,
            alg: testKey.Alg,
            kty: "EC");

        provider.AddKey(envelope, jwk);

        var keys = await provider.ListKeysAsync();
        keys.Should().HaveCount(1);
        keys[0].KeyId.Should().Be("kms-envelope-key-1");

        var health = await provider.CheckHealthAsync();
        health.IsHealthy.Should().BeTrue();

        using var sigProvider = await provider.GetSignatureProviderAsync("kms-envelope-key-1");
        sigProvider.CanSign.Should().BeTrue();

        byte[] data = "Hello Symbolon KMS"u8.ToArray();
        byte[] sig = new byte[sigProvider.SignatureSize];
        sigProvider.Sign(data, sig);

        bool isValid = sigProvider.Verify(data, sig);
        isValid.Should().BeTrue();
    }
}
