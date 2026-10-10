using System.Text;
using FluentAssertions;
using Xunit;

namespace Achilles.Crypto.Tests;

public sealed class Es256SignatureProviderTests
{
    [Fact]
    public void Es256_SignAndVerify_ValidSignature_ReturnsTrue()
    {
        using var provider = Es256SignatureProvider.GenerateKey("test-ec-kid");
        byte[] input = Encoding.UTF8.GetBytes("The quick brown fox jumps over the lazy dog");
        byte[] signature = new byte[provider.SignatureSize];

        provider.Sign(input, signature);
        bool isValid = provider.Verify(input, signature);

        isValid.Should().BeTrue();
    }

    [Fact]
    public void Es256_Verify_TamperedSignature_ReturnsFalse()
    {
        using var provider = Es256SignatureProvider.GenerateKey("test-ec-kid");
        byte[] input = Encoding.UTF8.GetBytes("Hello, world!");
        byte[] signature = new byte[provider.SignatureSize];

        provider.Sign(input, signature);
        signature[0] ^= 0xFF; // tamper with signature byte

        bool isValid = provider.Verify(input, signature);
        isValid.Should().BeFalse();
    }

    [Fact]
    public void Es256_Verify_TamperedPayload_ReturnsFalse()
    {
        using var provider = Es256SignatureProvider.GenerateKey("test-ec-kid");
        byte[] input = Encoding.UTF8.GetBytes("Original Payload");
        byte[] tamperedInput = Encoding.UTF8.GetBytes("Tampered Payload");
        byte[] signature = new byte[provider.SignatureSize];

        provider.Sign(input, signature);

        bool isValid = provider.Verify(tamperedInput, signature);
        isValid.Should().BeFalse();
    }

    [Fact]
    public void Es256_ExportAndImportJwk_VerifiesSuccessfully()
    {
        using var key = Es256SignatureProvider.GenerateKey("test-ec-kid");
        var jwk = key.ExportPublicJwk();

        jwk.Kty.Should().Be("EC");
        jwk.Crv.Should().Be("P-256");
        jwk.Alg.Should().Be(Alg.Es256);
        jwk.Kid.Should().Be("test-ec-kid");
        jwk.X.Should().NotBeNullOrWhiteSpace();
        jwk.Y.Should().NotBeNullOrWhiteSpace();

        using var importedVerifier = Es256SignatureProvider.ImportJwk(jwk);
        importedVerifier.CanSign.Should().BeFalse();

        byte[] input = Encoding.UTF8.GetBytes("Verify with imported public JWK");
        byte[] signature = new byte[key.SignatureSize];
        key.Sign(input, signature);

        importedVerifier.Verify(input, signature).Should().BeTrue();
    }
}
