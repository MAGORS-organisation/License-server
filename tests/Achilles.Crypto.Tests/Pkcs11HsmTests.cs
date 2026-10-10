using FluentAssertions;
using Achilles.Crypto.Kms;
using Xunit;

namespace Achilles.Crypto.Tests;

public sealed class Pkcs11HsmTests
{
    [Fact]
    public async Task Pkcs11Hsm_MultipleKeys_ManageableAndAccessible()
    {
        using var hsm = new Pkcs11HsmProvider(slotId: 2, tokenLabel: "YubiKey-HSM-Partition");

        using var key1 = Es256SignatureProvider.GenerateKey("yubikey-ec-01");
        using var key2 = Es256SignatureProvider.GenerateKey("yubikey-ec-02");

        hsm.ProvisionKey(key1);
        hsm.ProvisionKey(key2);

        var keys = await hsm.ListKeysAsync();
        keys.Should().HaveCount(2);

        var signer1 = await hsm.GetSignatureProviderAsync("yubikey-ec-01");
        var signer2 = await hsm.GetSignatureProviderAsync("yubikey-ec-02");

        signer1.Kid.Should().Be("yubikey-ec-01");
        signer2.Kid.Should().Be("yubikey-ec-02");

        byte[] data = "Hardware Token License"u8.ToArray();
        byte[] sig1 = new byte[signer1.SignatureSize];
        byte[] sig2 = new byte[signer2.SignatureSize];

        signer1.Sign(data, sig1);
        signer2.Sign(data, sig2);

        signer1.Verify(data, sig1).Should().BeTrue();
        signer2.Verify(data, sig2).Should().BeTrue();

        // Cross verification must fail
        signer1.Verify(data, sig2).Should().BeFalse();
    }

    [Fact]
    public async Task Pkcs11Hsm_ExportPublicJwk_MatchesKeyParameters()
    {
        using var hsm = new Pkcs11HsmProvider(slotId: 0, tokenLabel: "Nitrokey-HSM");
        using var key = Es256SignatureProvider.GenerateKey("nitro-01");

        hsm.ProvisionKey(key);

        var meta = await hsm.GetKeyMetadataAsync("nitro-01");
        meta.PublicKey.Should().NotBeNull();
        meta.PublicKey.Kid.Should().Be("nitro-01");
        meta.PublicKey.Kty.Should().Be("EC");
        meta.PublicKey.Crv.Should().Be("P-256");
        meta.KeyLocation.Should().Contain("pkcs11:slot-id=0");
    }

    [Fact]
    public async Task Pkcs11Hsm_Dispose_SafelyCleansUpManagedKeys()
    {
        var hsm = new Pkcs11HsmProvider(slotId: 0, tokenLabel: "Dispose-Test");
        using var key = Es256SignatureProvider.GenerateKey("temp-key");
        hsm.ProvisionKey(key);

        hsm.Dispose();

        Func<Task> act = async () => await hsm.ListKeysAsync();
        await act.Should().NotThrowAsync();
    }
}
