using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Achilles.Cli.Tests;

public sealed class ChaosCommandsTests
{
    [Fact]
    public async Task ChaosHelp_ReturnsZero()
    {
        int exitCode = await Program.Main(["chaos", "--help"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task ChaosRun_DefaultTable_ExecutesCleanly()
    {
        int exitCode = await Program.Main(["chaos", "run", "--duration", "1"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task ChaosRun_JsonFormat_ExecutesCleanly()
    {
        int exitCode = await Program.Main(["chaos", "run", "--format", "json", "--duration", "1"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task ChaosRun_SpecificScenario_ExecutesCleanly()
    {
        int exitCode = await Program.Main(["chaos", "run", "--scenario", "PARTITION", "--duration", "1"]);
        exitCode.Should().Be(0);
    }
}
