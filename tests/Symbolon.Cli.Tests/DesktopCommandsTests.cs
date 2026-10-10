using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Symbolon.Cli.Tests;

public sealed class DesktopCommandsTests
{
    [Fact]
    public async Task DesktopHelp_ReturnsZero()
    {
        int exitCode = await Program.Main(["desktop", "--help"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task StudioHelp_ReturnsZero()
    {
        int exitCode = await Program.Main(["studio", "--help"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task DesktopProcesses_ExecutesCleanly()
    {
        int exitCode = await Program.Main(["desktop", "processes"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task DesktopInspect_OfflineDaemon_ReturnsCleanly()
    {
        // When agent is not running on custom port, inspect handles gracefully
        int exitCode = await Program.Main(["desktop", "inspect", "--port", "59999"]);
        exitCode.Should().Be(0);
    }
}
