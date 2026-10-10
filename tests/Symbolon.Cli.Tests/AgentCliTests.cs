using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Symbolon.Cli.Tests;

public sealed class AgentCliTests
{
    [Fact]
    public async Task AgentHelp_ReturnsZero()
    {
        int exitCode = await Program.Main(["agent", "--help"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task Agent_WithoutArgs_ReturnsZero()
    {
        int exitCode = await Program.Main(["agent"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task AgentStatus_WhenOffline_ReturnsNonZero()
    {
        int exitCode = await Program.Main(["agent", "status", "--port", "59999"]);
        exitCode.Should().Be(1);
    }
}
