using Symbolon.Protocol.Replication;

namespace Symbolon.Domain.Replication;

/// <summary>
/// Contract for Active-Active multi-region geo-replication engine.
/// Coordinates distributed vector clocks, PN-Counter CRDTs, disjoint seat spaces, and delta synchronization.
/// </summary>
public interface IGeoReplicationEngine
{
    string LocalRegionId { get; }
    VectorClock CurrentClock { get; }
    PnCounter CurrentCounter { get; }

    void RegisterPeer(string regionId, string endpoint, int seatRangeStart, int seatRangeEnd);
    IReadOnlyList<RegionPeer> GetPeers();

    SeatAllocation? TryAllocateLocalSeat(
        string licenseId,
        string fingerprint,
        string? machineId,
        TimeSpan ttl,
        DateTimeOffset now);

    bool TryReleaseLocalSeat(string leaseId, DateTimeOffset now);

    ReplicationDelta GenerateDeltaForPeer(string targetRegionId, VectorClock? peerClock, DateTimeOffset now);

    ReplicationSyncResponse AssimilateRemoteDelta(ReplicationDelta delta, DateTimeOffset now);

    IReadOnlyList<ReplicatedSeatState> GetGlobalActiveSeats(string? licenseId = null, DateTimeOffset? now = null);

    ClusterStatusDto GetStatus(DateTimeOffset now);
}
