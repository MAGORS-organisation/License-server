using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Xunit;

namespace Achilles.Crypto.Tests;

public sealed class SlhDsaSignatureProviderTests
{
    [Fact]
    public void SlhDsa128s_SignAndVerify_WhenSupported_WorksCorrectly()
    {
        if (!SlhDsaSignatureProvider.IsSupported)
        {
            return;
        }

        using var provider = SlhDsaSignatureProvider.GenerateKey(SlhDsaAlgorithm.SlhDsaSha2_128s, "test-slh-kid");
        provider.Alg.Should().Be(Alg.SlhDsaSha2128s);
        provider.SignatureSize.Should().BeGreaterThan(0);
        provider.CanSign.Should().BeTrue();

        byte[] input = Encoding.UTF8.GetBytes("FIPS 205 Stateless Hash-Based Digital Signature test");
        byte[] signature = new byte[provider.SignatureSize];

        provider.Sign(input, signature);
        bool isValid = provider.Verify(input, signature);
        isValid.Should().BeTrue();
    }

    [Fact]
    public void SlhDsa128s_Verify_TamperedSignature_ReturnsFalse()
    {
        if (!SlhDsaSignatureProvider.IsSupported)
        {
            return;
        }

        using var provider = SlhDsaSignatureProvider.GenerateKey(SlhDsaAlgorithm.SlhDsaSha2_128s, "test-slh-kid");
        byte[] input = Encoding.UTF8.GetBytes("FIPS 205 Stateless Hash-Based Digital Signature test");
        byte[] signature = new byte[provider.SignatureSize];

        provider.Sign(input, signature);
        signature[15] ^= 0xFF; // corrupt signature byte

        bool isValid = provider.Verify(input, signature);
        isValid.Should().BeFalse();
    }

    [Fact]
    public void SlhDsa_ExportAndImportJwk_VerifiesSuccessfully()
    {
        if (!SlhDsaSignatureProvider.IsSupported)
        {
            return;
        }

        using var key = SlhDsaSignatureProvider.GenerateKey(SlhDsaAlgorithm.SlhDsaSha2_128s, "test-slh-jwk");
        var jwk = key.ExportPublicJwk();

        jwk.Kty.Should().Be("AKP");
        jwk.Alg.Should().Be(Alg.SlhDsaSha2128s);
        jwk.Kid.Should().Be("test-slh-jwk");
        jwk.Pub.Should().NotBeNullOrWhiteSpace();

        using var imported = SlhDsaSignatureProvider.ImportJwk(jwk);
        imported.CanSign.Should().BeFalse();

        byte[] input = Encoding.UTF8.GetBytes("Testing FIPS 205 SLH-DSA JWK import");
        byte[] signature = new byte[key.SignatureSize];
        key.Sign(input, signature);

        imported.Verify(input, signature).Should().BeTrue();
    }

    [Fact]
    public void SlhDsa128f_FastVariant_SignAndVerify_Succeeds()
    {
        if (!SlhDsaSignatureProvider.IsSupported)
        {
            return;
        }

        using var provider = SlhDsaSignatureProvider.GenerateKey(SlhDsaAlgorithm.SlhDsaSha2_128f, "test-slh-fast");
        provider.Alg.Should().Be(Alg.SlhDsaSha2128f);

        byte[] input = Encoding.UTF8.GetBytes("Testing FIPS 205 Fast variant");
        byte[] signature = new byte[provider.SignatureSize];

        provider.Sign(input, signature);
        provider.Verify(input, signature).Should().BeTrue();
    }
}
