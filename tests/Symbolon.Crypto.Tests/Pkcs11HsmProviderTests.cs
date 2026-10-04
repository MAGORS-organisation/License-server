using System.Security.Cryptography;
using FluentAssertions;
using Symbolon.Crypto;
using Symbolon.Crypto.Kms;
using Xunit;

namespace Symbolon.Crypto.Tests;

public sealed class Pkcs11HsmProviderTests
{
    [Fact]
    public async Task Pkcs11HsmProvider_CheckHealth_ReportsOperational()
    {
        using var hsm = new Pkcs11HsmProvider(slotId: 1, tokenLabel: "FIPS-Partition-01");
        var health = await hsm.CheckHealthAsync();

        health.IsHealthy.Should().BeTrue();
        health.ProviderType.Should().Be(KmsProviderType.Pkcs11Hsm);
        health.Details.Should().Contain("Slot 1");
    }

    [Fact]
    public async Task Pkcs11HsmProvider_ProvisionKey_AndSignVerify_Succeeds()
    {
        using var hsm = new Pkcs11HsmProvider(slotId: 0, tokenLabel: "Token-Test");
        using var ecKey = Es256SignatureProvider.GenerateKey("hsm-key-01");

        hsm.ProvisionKey(ecKey);

        var keys = await hsm.ListKeysAsync();
        keys.Should().ContainSingle(k => k.KeyId == "hsm-key-01");

        var signer = await hsm.GetSignatureProviderAsync("hsm-key-01");
        signer.Kid.Should().Be("hsm-key-01");
        signer.Alg.Should().Be(Alg.Es256);
        signer.CanSign.Should().BeTrue();

        byte[] payload = "Critical License Document Payload"u8.ToArray();
        byte[] signature = new byte[signer.SignatureSize];

        signer.Sign(payload, signature);
        bool isValid = signer.Verify(payload, signature);
        isValid.Should().BeTrue();

        // Tamper test
        byte[] tampered = "Tampered License Document Payload"u8.ToArray();
        bool isTamperedValid = signer.Verify(tampered, signature);
        isTamperedValid.Should().BeFalse();
    }

    [Fact]
    public async Task Pkcs11HsmProvider_MLDSA_ProvisionAndSign_WhenSupported()
    {
        if (!MlDsaSignatureProvider.IsSupported)
        {
            return;
        }

        using var hsm = new Pkcs11HsmProvider(slotId: 0, tokenLabel: "Token-PQC");
        using var pqKey = MlDsaSignatureProvider.GenerateKey(MLDsaAlgorithm.MLDsa65, "hsm-pqc-01");

        hsm.ProvisionKey(pqKey);

        var signer = await hsm.GetSignatureProviderAsync("hsm-pqc-01");
        signer.Alg.Should().Be(Alg.MlDsa65);
        signer.SignatureSize.Should().Be(3309);

        byte[] payload = "Post-Quantum Signed Contract"u8.ToArray();
        byte[] sig = new byte[signer.SignatureSize];

        signer.Sign(payload, sig);
        signer.Verify(payload, sig).Should().BeTrue();
    }

    [Fact]
    public async Task Pkcs11HsmProvider_GetUnknownKey_ThrowsKeyNotFoundException()
    {
        using var hsm = new Pkcs11HsmProvider();
        Func<Task> act = async () => await hsm.GetSignatureProviderAsync("non-existent-key");
        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
