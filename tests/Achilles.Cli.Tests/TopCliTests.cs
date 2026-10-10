using System.Threading.Tasks;
using FluentAssertions;
using Achilles.Cli.Commands;
using Achilles.Protocol.Reporting;
using Xunit;

namespace Achilles.Cli.Tests;

public sealed class TopCliTests
{
    private static readonly int[] SampleSparkline = [1, 2, 3, 5, 4, 3, 2, 6, 4, 2];

    [Fact]
    public async Task TopHelp_ReturnsZero()
    {
        int exitCode = await Program.Main(["top", "--help"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public void RenderDashboard_WithSampleData_RendersSuccessfully()
    {
        var stats = new TopSystemStatsDto(
            DateTimeOffset.UtcNow,
            12345.0,
            5,
            20,
            2,
            0,
            0,
            150,
            true,
            SampleSparkline);

        var leases = new List<ActiveLeaseItemDto>
        {
            new(1, "lease-101", "lic-demo", "tenant-1", 1, "0123456789ABCDEF", "machine-srv01", "dev_user", DateTimeOffset.UtcNow.AddMinutes(-10), DateTimeOffset.UtcNow.AddHours(2), false, null, "CAD Pro Enterprise"),
            new(2, "lease-102", "lic-demo", "tenant-1", 2, "FEDCBA9876543210", "laptop-field", "field_engineer", DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(3), true, DateTimeOffset.UtcNow.AddDays(3), "CAD Pro Enterprise")
        };

        // Render dashboard to verify it formats without throwing exceptions
        var act = () => TopCommand.RenderDashboard(stats, leases, filter: null, isPaused: false);
        act.Should().NotThrow();
    }
}
