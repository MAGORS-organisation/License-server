using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Symbolon.Cli.Tests;

public sealed class ExperimentsCliTests
{
    [Fact]
    public async Task Experiments_Help_ReturnsZero()
    {
        int exitCode = await Program.Main(["experiments", "--help"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task Experiments_NoArgs_ReturnsZero()
    {
        int exitCode = await Program.Main(["experiments"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task Experiments_UnknownCommand_ReturnsOne()
    {
        int exitCode = await Program.Main(["experiments", "invalid-subcommand"]);
        exitCode.Should().Be(1);
    }

    [Fact]
    public async Task Experiments_Create_WithoutRequiredFlags_ReturnsOne()
    {
        int exitCode = await Program.Main(["experiments", "create"]);
        exitCode.Should().Be(1);
    }

    [Fact]
    public async Task Experiments_Start_WithoutId_ReturnsOne()
    {
        int exitCode = await Program.Main(["experiments", "start"]);
        exitCode.Should().Be(1);
    }

    [Fact]
    public async Task Experiments_Pause_WithoutId_ReturnsOne()
    {
        int exitCode = await Program.Main(["experiments", "pause"]);
        exitCode.Should().Be(1);
    }

    [Fact]
    public async Task Experiments_Promote_WithoutVariant_ReturnsOne()
    {
        int exitCode = await Program.Main(["experiments", "promote", "exp-1"]);
        exitCode.Should().Be(1);
    }

    [Fact]
    public async Task Experiments_Rollback_WithoutId_ReturnsOne()
    {
        int exitCode = await Program.Main(["experiments", "rollback"]);
        exitCode.Should().Be(1);
    }

    [Fact]
    public async Task Experiments_Report_WithoutId_ReturnsOne()
    {
        int exitCode = await Program.Main(["experiments", "report"]);
        exitCode.Should().Be(1);
    }

    [Fact]
    public async Task Experiments_Simulate_WithoutId_ReturnsOne()
    {
        int exitCode = await Program.Main(["experiments", "simulate"]);
        exitCode.Should().Be(1);
    }
}
