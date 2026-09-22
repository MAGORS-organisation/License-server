using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Symbolon.ControlPlane.Endpoints;
using Symbolon.Protocol.Replication;
using Xunit;

namespace Symbolon.ControlPlane.Tests;

public class ReplicationApiTests : IClassFixture<ControlPlaneFactory>
{
    private readonly HttpClient _client;

    public ReplicationApiTests(ControlPlaneFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetClusterStatus_Returns200_WithTopologyAndClocks()
    {
        var response = await _client.GetAsync(new Uri("/v1/replication/status", UriKind.Relative));
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var status = await response.Content.ReadFromJsonAsync<ClusterStatusDto>();
        status.Should().NotBeNull();
        status!.LocalRegionId.Should().Be("eu-central-1");
        status.LocalClock.Should().NotBeNull();
        status.Peers.Should().NotBeEmpty();
        status.TotalDisjointCapacity.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task RegisterPeerRegion_Returns200_AndRegistersNewRegion()
    {
        var newPeer = new RegionPeer(
            "sa-east-1",
            "https://sa.symbolon.internal/v1/replication",
            301,
            400,
            DateTimeOffset.UtcNow,
            IsHealthy: true,
            LatencyMs: 45);

        var response = await _client.PostAsJsonAsync(new Uri("/v1/replication/peers", UriKind.Relative), newPeer);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify status reflects the newly registered peer
        var statusResponse = await _client.GetAsync(new Uri("/v1/replication/status", UriKind.Relative));
        var status = await statusResponse.Content.ReadFromJsonAsync<ClusterStatusDto>();
        status!.Peers.Should().Contain(p => p.RegionId == "sa-east-1");
    }

    [Fact]
    public async Task SyncClusterDeltas_RejectsInvalidHmac_With401Unauthorized()
    {
        var now = DateTimeOffset.UtcNow;
        var clock = VectorClock.Empty.Increment("us-east-1");
        var delta = new ReplicationDelta("d_test_01", "us-east-1", clock, now, [], PnCounter.Empty);

        var request = new ReplicationSyncRequest(
            "us-east-1",
            "eu-central-1",
            delta,
            now,
            "INVALID_SIGNATURE_HEX");

        var response = await _client.PostAsJsonAsync(new Uri("/v1/replication/sync", UriKind.Relative), request);
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SyncClusterDeltas_RejectsExpiredTimestamp_With400BadRequest()
    {
        var pastTime = DateTimeOffset.UtcNow.AddMinutes(-10); // 10m drift > 5m allowed
        var clock = VectorClock.Empty.Increment("us-east-1");
        var delta = new ReplicationDelta("d_test_02", "us-east-1", clock, pastTime, [], PnCounter.Empty);

        byte[] payload = ReplicationSecurity.CreateCanonicalPayload("us-east-1", "eu-central-1", delta.DeltaId, pastTime);
        string hmac = ReplicationSecurity.ComputeHmac(payload, ReplicationEndpoints.DefaultClusterSecret);

        var request = new ReplicationSyncRequest(
            "us-east-1",
            "eu-central-1",
            delta,
            pastTime,
            hmac);

        var response = await _client.PostAsJsonAsync(new Uri("/v1/replication/sync", UriKind.Relative), request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SyncClusterDeltas_AcceptsValidDelta_ReturnsMergedClockAndReciprocalDelta()
    {
        var now = DateTimeOffset.UtcNow;
        var clock = VectorClock.Empty.Increment("us-east-1");
        var remoteSeat = new ReplicatedSeatState
        {
            SeatId = "seat_us_101",
            SeatNo = 101,
            LicenseId = "lic_enterprise_geo",
            LeaseId = "l_us_sync_test",
            HolderFingerprint = "fp_us_client",
            OriginRegionId = "us-east-1",
            AcquiredAt = now,
            ExpiresAt = now.AddMinutes(30),
            LeaseSeq = 1,
            IsReleased = false,
            LamportClock = 1
        };

        var delta = new ReplicationDelta(
            $"d_sync_{Guid.NewGuid():N}",
            "us-east-1",
            clock,
            now,
            [remoteSeat],
            PnCounter.Empty.Increment("us-east-1", 1));

        byte[] payload = ReplicationSecurity.CreateCanonicalPayload("us-east-1", "eu-central-1", delta.DeltaId, now);
        string hmac = ReplicationSecurity.ComputeHmac(payload, ReplicationEndpoints.DefaultClusterSecret);

        var request = new ReplicationSyncRequest(
            "us-east-1",
            "eu-central-1",
            delta,
            now,
            hmac);

        var response = await _client.PostAsJsonAsync(new Uri("/v1/replication/sync", UriKind.Relative), request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var syncRes = await response.Content.ReadFromJsonAsync<ReplicationSyncResponse>();
        syncRes.Should().NotBeNull();
        syncRes!.Success.Should().BeTrue();
        syncRes.MergedClock.Should().NotBeNull();
        syncRes.MergedClock.GetClock("us-east-1").Should().Be(1);
        syncRes.MergedClock.GetClock("eu-central-1").Should().BeGreaterThan(0);
        syncRes.ReciprocalDelta.Should().NotBeNull();
    }
}
