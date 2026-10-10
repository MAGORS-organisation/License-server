using FluentAssertions;
using Xunit;

namespace Achilles.Cli.Tests;

public sealed class ClusterCliTests
{
    [Fact]
    public async Task Cluster_Status_Default_Succeeds()
    {
        int exitCode = await Program.Main(["cluster", "status"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task Cluster_Status_CustomRegion_Succeeds()
    {
        int exitCode = await Program.Main([
            "cluster", "status",
            "--region", "ap-southeast-1",
            "--range-start", "201",
            "--range-end", "300"
        ]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task Cluster_Help_Succeeds()
    {
        int exitCode = await Program.Main(["cluster", "--help"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task Cluster_Sync_WithUnreachablePeer_HandlesGracefully()
    {
        // Should catch network exception and return gracefully
        int exitCode = await Program.Main([
            "cluster", "sync",
            "--peer", "http://127.0.0.1:59999",
            "--region", "eu-central-1"
        ]);
        exitCode.Should().Be(0);
    }
}
