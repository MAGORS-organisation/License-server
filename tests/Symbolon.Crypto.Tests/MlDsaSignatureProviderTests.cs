using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Xunit;

namespace Symbolon.Crypto.Tests;

public sealed class MlDsaSignatureProviderTests
{
    [Fact]
    public void MlDsa65_SignAndVerify_WhenSupported_WorksCorrectly()
    {
        if (!MlDsaSignatureProvider.IsSupported)
        {
            // Platform doesn't have CNG PQC / OpenSSL 3.5+ yet
            return;
        }

        using var provider = MlDsaSignatureProvider.GenerateKey(MLDsaAlgorithm.MLDsa65, "test-pq-kid");
        provider.Alg.Should().Be(Alg.MlDsa65);
        provider.SignatureSize.Should().Be(3309); // ML-DSA-65 signature size in bytes

        byte[] input = Encoding.UTF8.GetBytes("Post-Quantum digital signature test");
        byte[] signature = new byte[provider.SignatureSize];

        provider.Sign(input, signature);
        bool isValid = provider.Verify(input, signature);
        isValid.Should().BeTrue();
    }

    [Fact]
    public void MlDsa65_Verify_TamperedSignature_ReturnsFalse()
    {
        if (!MlDsaSignatureProvider.IsSupported)
        {
            return;
        }

        using var provider = MlDsaSignatureProvider.GenerateKey(MLDsaAlgorithm.MLDsa65, "test-pq-kid");
        byte[] input = Encoding.UTF8.GetBytes("Post-Quantum digital signature test");
        byte[] signature = new byte[provider.SignatureSize];

        provider.Sign(input, signature);
        signature[10] ^= 0x5A; // corrupt signature byte

        bool isValid = provider.Verify(input, signature);
        isValid.Should().BeFalse();
    }

    [Fact]
    public void MlDsa65_ExportAndImportJwk_VerifiesSuccessfully()
    {
        if (!MlDsaSignatureProvider.IsSupported)
        {
            return;
        }

        using var key = MlDsaSignatureProvider.GenerateKey(MLDsaAlgorithm.MLDsa65, "test-pq-kid");
        var jwk = key.ExportPublicJwk();

        jwk.Kty.Should().Be("AKP"); // RFC 9964
        jwk.Alg.Should().Be(Alg.MlDsa65);
        jwk.Kid.Should().Be("test-pq-kid");
        jwk.Pub.Should().NotBeNullOrWhiteSpace();

        using var imported = MlDsaSignatureProvider.ImportJwk(jwk);
        imported.CanSign.Should().BeFalse();

        byte[] input = Encoding.UTF8.GetBytes("Testing AKP JWK import");
        byte[] signature = new byte[key.SignatureSize];
        key.Sign(input, signature);

        imported.Verify(input, signature).Should().BeTrue();
    }
}
