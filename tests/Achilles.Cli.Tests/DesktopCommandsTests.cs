using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Achilles.Cli.Tests;

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

    [Fact]
    public async Task DesktopCompanion_OnceMode_ReturnsZero()
    {
        int exitCode = await Program.Main(["desktop", "companion", "--port", "59999", "--once"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task DesktopBorrow_OfflineDaemon_ReturnsNonZero()
    {
        int exitCode = await Program.Main(["desktop", "borrow", "--days", "7", "--port", "59999"]);
        exitCode.Should().Be(1);
    }

    [Fact]
    public async Task DesktopReturn_OfflineDaemon_ReturnsNonZero()
    {
        int exitCode = await Program.Main(["desktop", "return", "--port", "59999"]);
        exitCode.Should().Be(1);
    }

    [Fact]
    public async Task DesktopNotify_ValidParameters_ReturnsZero()
    {
        int exitCode = await Program.Main(["desktop", "notify", "--title", "Achilles Test", "--message", "Unit test notification", "--level", "info"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task DesktopUnknownSubcommand_ReturnsOne()
    {
        int exitCode = await Program.Main(["desktop", "invalid-subcommand"]);
        exitCode.Should().Be(1);
    }
}
