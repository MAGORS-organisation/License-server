using System.Security.Cryptography;
using FluentAssertions;
using Xunit;

namespace Symbolon.Crypto.Tests;

public class MlKemKeyEncapsulationTests
{
    [Fact]
    public void MlKem768_GenerateKey_EncapsulateDecapsulate_DerivesMatchingSharedSecret()
    {
        if (!MlKemKeyEncapsulationProvider.IsSupported)
        {
            return; // Skip if platform does not support ML-KEM
        }

        using var recipientProvider = MlKemKeyEncapsulationProvider.GenerateKey(MLKemAlgorithm.MLKem768, "recipient-kem-768");
        recipientProvider.Alg.Should().Be(Alg.MlKem768);
        recipientProvider.CanDecapsulate.Should().BeTrue();
        recipientProvider.CiphertextSize.Should().Be(1088);
        recipientProvider.SharedSecretSize.Should().Be(32);

        // Sender encapsulates
        var (ciphertext, senderSharedSecret) = recipientProvider.Encapsulate();
        ciphertext.Length.Should().Be(1088);
        senderSharedSecret.Length.Should().Be(32);

        // Recipient decapsulates
        byte[] recipientSharedSecret = recipientProvider.Decapsulate(ciphertext);
        recipientSharedSecret.Length.Should().Be(32);

        // Cryptographic invariant: Shared secrets must be strictly identical
        recipientSharedSecret.Should().Equal(senderSharedSecret);
    }

    [Fact]
    public void MlKem1024_GenerateKey_EncapsulateDecapsulate_DerivesMatchingSharedSecret()
    {
        if (!MlKemKeyEncapsulationProvider.IsSupported)
        {
            return;
        }

        using var recipientProvider = MlKemKeyEncapsulationProvider.GenerateKey(MLKemAlgorithm.MLKem1024, "recipient-kem-1024");
        recipientProvider.Alg.Should().Be(Alg.MlKem1024);
        recipientProvider.CanDecapsulate.Should().BeTrue();
        recipientProvider.CiphertextSize.Should().Be(1568);
        recipientProvider.SharedSecretSize.Should().Be(32);

        var (ciphertext, senderSharedSecret) = recipientProvider.Encapsulate();
        ciphertext.Length.Should().Be(1568);
        senderSharedSecret.Length.Should().Be(32);

        byte[] recipientSharedSecret = recipientProvider.Decapsulate(ciphertext);
        recipientSharedSecret.Should().Equal(senderSharedSecret);
    }

    [Fact]
    public void MlKem_JwkExportImport_RoundTrip_WorksForPublicKey()
    {
        if (!MlKemKeyEncapsulationProvider.IsSupported) return;

        using var original = MlKemKeyEncapsulationProvider.GenerateKey(MLKemAlgorithm.MLKem768, "kem-jwk-test");
        var jwk = original.ExportJwk(includePrivate: false);

        jwk.Kty.Should().Be("AKP");
        jwk.Alg.Should().Be(Alg.MlKem768);
        jwk.Kid.Should().Be("kem-jwk-test");
        jwk.Use.Should().Be("enc");
        jwk.Pub.Should().NotBeNullOrWhiteSpace();
        jwk.Priv.Should().BeNull();

        using var importedPublic = MlKemKeyEncapsulationProvider.ImportJwk(jwk);
        importedPublic.CanDecapsulate.Should().BeFalse();
        importedPublic.Alg.Should().Be(Alg.MlKem768);

        // Public key can encapsulate
        var (ct, senderSecret) = importedPublic.Encapsulate();
        // Original private key can decapsulate
        byte[] recipientSecret = original.Decapsulate(ct);
        recipientSecret.Should().Equal(senderSecret);
    }

    [Fact]
    public void MlKem_JwkExportImport_RoundTrip_WorksForPrivateKey()
    {
        if (!MlKemKeyEncapsulationProvider.IsSupported) return;

        using var original = MlKemKeyEncapsulationProvider.GenerateKey(MLKemAlgorithm.MLKem768, "kem-full-jwk");
        var jwk = original.ExportJwk(includePrivate: true);

        jwk.Priv.Should().NotBeNullOrWhiteSpace();

        using var importedFull = MlKemKeyEncapsulationProvider.ImportJwk(jwk);
        importedFull.CanDecapsulate.Should().BeTrue();

        var (ct, senderSecret) = importedFull.Encapsulate();
        byte[] recipientSecret = importedFull.Decapsulate(ct);
        recipientSecret.Should().Equal(senderSecret);
    }
}
