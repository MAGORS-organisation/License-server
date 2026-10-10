using FluentAssertions;
using Achilles.Domain.Replication;
using Xunit;

namespace Achilles.Domain.Tests;

public sealed class DisasterRecoveryTests
{
    [Fact]
    public void FailoverAndReclaimPeerSeats_ReclaimsCapacityAndAdvancesVectorClock()
    {
        var engine = new GeoReplicationEngine("eu-central-1", seatRangeStart: 1, seatRangeEnd: 100);
        engine.RegisterPeer("us-east-1", "https://us.symbolon.internal/v1/replication", seatRangeStart: 101, seatRangeEnd: 200);

        var now = DateTimeOffset.UtcNow;
        var initialStatus = engine.GetStatus(now);
        initialStatus.TotalDisjointCapacity.Should().Be(200);

        // Execute failover of failed region us-east-1
        var report = engine.FailoverAndReclaimPeerSeats("us-east-1", now);

        report.Should().NotBeNull();
        report.PromotedRegionId.Should().Be("eu-central-1");
        report.FailedRegionId.Should().Be("us-east-1");
        report.ReclaimedSeatsCount.Should().Be(100);
        report.VectorClockAdvancedBy.Should().Be(1);
        report.QuorumMaintained.Should().BeTrue();

        // Verify peer marked unhealthy
        var peers = engine.GetPeers();
        var failedPeer = peers.Single(p => p.RegionId == "us-east-1");
        failedPeer.IsHealthy.Should().BeFalse();

        // Verify vector clock advanced
        engine.CurrentClock.GetClock("eu-central-1").Should().BeGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void FailoverAndReclaimPeerSeats_UnknownRegion_ThrowsKeyNotFoundException()
    {
        var engine = new GeoReplicationEngine("eu-central-1", seatRangeStart: 1, seatRangeEnd: 50);

        var act = () => engine.FailoverAndReclaimPeerSeats("ap-south-1", DateTimeOffset.UtcNow);
        act.Should().Throw<KeyNotFoundException>();
    }
}
