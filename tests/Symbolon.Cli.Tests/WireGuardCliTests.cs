using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Symbolon.Cli.Tests;

public sealed class WireGuardCliTests
{
    [Fact]
    public async Task MeshHelp_ReturnsZero()
    {
        int exitCode = await Program.Main(["mesh", "--help"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task MeshWireGuardHelp_ReturnsZero()
    {
        int exitCode = await Program.Main(["mesh", "wireguard", "--help"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task MeshWireGuardConfig_GeneratesOutput()
    {
        int exitCode = await Program.Main(["mesh", "wireguard", "config"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task MeshWireGuardStatus_ExecutesCleanly()
    {
        int exitCode = await Program.Main(["mesh", "wireguard", "status"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task MeshWireGuardRotate_ExecutesCleanly()
    {
        int exitCode = await Program.Main(["mesh", "wireguard", "rotate"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task MeshMtlsRotate_ExecutesCleanly()
    {
        int exitCode = await Program.Main(["mesh", "mtls", "rotate"]);
        exitCode.Should().Be(0);
    }
}
