using FluentAssertions;
using Achilles.Relay.Mesh;
using Xunit;

namespace Achilles.Relay.Tests;

public sealed class WireGuardMeshTests
{
    [Fact]
    public void Coordinator_Generates_Valid_Keys_And_Config()
    {
        var coordinator = new WireGuardMeshCoordinator("rly-node-test-1", "10.100.0.1/24", 51820);
        var config = coordinator.GetNodeConfig();

        config.NodeId.Should().Be("rly-node-test-1");
        config.PrivateKey.Should().NotBeNullOrWhiteSpace();
        config.PublicKey.Should().NotBeNullOrWhiteSpace();
        config.OverlayIp.Should().Be("10.100.0.1/24");
        config.ListenPort.Should().Be(51820);

        string rawConfig = coordinator.GenerateConfigFile();
        rawConfig.Should().Contain("[Interface]");
        rawConfig.Should().Contain($"PrivateKey = {config.PrivateKey}");
        rawConfig.Should().Contain("Address = 10.100.0.1/24");
        rawConfig.Should().Contain("ListenPort = 51820");
    }

    [Fact]
    public void Coordinator_Adds_Peers_And_Exports_WgConfig_With_Peers()
    {
        var coordinator = new WireGuardMeshCoordinator("rly-local-01", "10.100.0.1/24", 51820);
        coordinator.AddOrUpdatePeer("rly-peer-ba", "PubKeyPeerBA1234567890=", "192.168.1.50:51820", "10.100.0.2/32");

        var peers = coordinator.GetPeers();
        peers.Should().HaveCount(1);
        peers[0].NodeId.Should().Be("rly-peer-ba");
        peers[0].IsConnected.Should().BeTrue();

        string rawConfig = coordinator.GenerateConfigFile();
        rawConfig.Should().Contain("[Peer]");
        rawConfig.Should().Contain("PublicKey = PubKeyPeerBA1234567890=");
        rawConfig.Should().Contain("Endpoint = 192.168.1.50:51820");
        rawConfig.Should().Contain("AllowedIPs = 10.100.0.2/32");
        rawConfig.Should().Contain("PersistentKeepalive = 25");
    }

    [Fact]
    public void Coordinator_Rotates_WireGuard_Keys()
    {
        var coordinator = new WireGuardMeshCoordinator("rly-rotate-01");
        var initialConfig = coordinator.GetNodeConfig();

        var rotatedConfig = coordinator.RotateWireGuardKeys();

        rotatedConfig.PrivateKey.Should().NotBe(initialConfig.PrivateKey);
        rotatedConfig.PublicKey.Should().NotBe(initialConfig.PublicKey);
    }

    [Fact]
    public void Coordinator_Generates_And_Rotates_Mtls_Certificates()
    {
        var coordinator = new WireGuardMeshCoordinator("rly-mtls-01");
        var initialMtls = coordinator.GetActiveMtlsCredentials();

        initialMtls.CertPem.Should().Contain("-----BEGIN CERTIFICATE-----");
        initialMtls.KeyPem.Should().Contain("-----BEGIN EC PRIVATE KEY-----");
        initialMtls.RotationEpoch.Should().Be(1);

        var rotatedMtls = coordinator.RotateMtlsKeys();
        rotatedMtls.RotationEpoch.Should().Be(2);
        rotatedMtls.CertPem.Should().NotBe(initialMtls.CertPem);
        rotatedMtls.KeyPem.Should().NotBe(initialMtls.KeyPem);
    }
}
