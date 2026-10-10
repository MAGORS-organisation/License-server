using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Symbolon.Domain.Replication;
using Xunit;

namespace Symbolon.ControlPlane.Tests;

public sealed class ReplicationFailoverApiTests : IClassFixture<ControlPlaneFactory>
{
    private readonly HttpClient _client;

    public ReplicationFailoverApiTests(ControlPlaneFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Failover_Endpoint_ExecutesFailoverAndReturnsReport()
    {
        var req = new DisasterRecoveryFailoverRequest(
            TargetRegion: "eu-central-1",
            FailedRegion: "us-east-1",
            RebalanceSeats: true);

        var response = await _client.PostAsJsonAsync("/v1/replication/failover", req);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var report = await response.Content.ReadFromJsonAsync<DisasterRecoveryReport>();
        report.Should().NotBeNull();
        report!.PromotedRegionId.Should().Be("eu-central-1");
        report.FailedRegionId.Should().Be("us-east-1");
        report.ReclaimedSeatsCount.Should().Be(100);
        report.QuorumMaintained.Should().BeTrue();
    }
}
