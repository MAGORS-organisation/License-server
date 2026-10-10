using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Achilles.Cli.Tests;

public sealed class ReportsCliTests
{
    [Fact]
    public async Task ReportsHelp_ReturnsZero()
    {
        int exitCode = await Program.Main(["reports", "--help"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task Reports_WithoutArgs_ReturnsZero()
    {
        int exitCode = await Program.Main(["reports"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task Reports_UnknownCommand_ReturnsNonZero()
    {
        int exitCode = await Program.Main(["reports", "nonexistent-command"]);
        exitCode.Should().Be(1);
    }
}
