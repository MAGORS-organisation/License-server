using System.Text.Json.Serialization;

namespace Achilles.Protocol.Replication;

/// <summary>
/// Replicated state of a single seat lease across distributed regions.
/// </summary>
public sealed record ReplicatedSeatState
{
    [JsonPropertyName("seatId")]
    public required string SeatId { get; init; }

    [JsonPropertyName("seatNo")]
    public required int SeatNo { get; init; }

    [JsonPropertyName("licenseId")]
    public required string LicenseId { get; init; }

    [JsonPropertyName("leaseId")]
    public string? LeaseId { get; init; }

    [JsonPropertyName("holderFingerprint")]
    public string? HolderFingerprint { get; init; }

    [JsonPropertyName("machineId")]
    public string? MachineId { get; init; }

    [JsonPropertyName("originRegionId")]
    public required string OriginRegionId { get; init; }

    [JsonPropertyName("acquiredAt")]
    public DateTimeOffset? AcquiredAt { get; init; }

    [JsonPropertyName("expiresAt")]
    public required DateTimeOffset ExpiresAt { get; init; }

    [JsonPropertyName("leaseSeq")]
    public long LeaseSeq { get; init; }

    [JsonPropertyName("isReleased")]
    public bool IsReleased { get; init; }

    [JsonPropertyName("lamportClock")]
    public long LamportClock { get; init; }
}

/// <summary>
/// Delta envelope exchanged between clusters during active-active replication.
/// </summary>
public sealed record ReplicationDelta(
    [property: JsonPropertyName("deltaId")] string DeltaId,
    [property: JsonPropertyName("sourceRegionId")] string SourceRegionId,
    [property: JsonPropertyName("vectorClock")] VectorClock VectorClock,
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp,
    [property: JsonPropertyName("seats")] IReadOnlyList<ReplicatedSeatState> Seats,
    [property: JsonPropertyName("counterState")] PnCounter CounterState);

/// <summary>
/// Cross-region synchronization request with cryptographic HMAC authentication and anti-replay window.
/// </summary>
public sealed record ReplicationSyncRequest(
    [property: JsonPropertyName("senderRegionId")] string SenderRegionId,
    [property: JsonPropertyName("targetRegionId")] string TargetRegionId,
    [property: JsonPropertyName("delta")] ReplicationDelta Delta,
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp,
    [property: JsonPropertyName("hmacSignature")] string HmacSignature);

/// <summary>
/// Response to replication sync containing reciprocal delta and merged vector clock.
/// </summary>
public sealed record ReplicationSyncResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("receiverRegionId")] string ReceiverRegionId,
    [property: JsonPropertyName("mergedClock")] VectorClock MergedClock,
    [property: JsonPropertyName("reciprocalDelta")] ReplicationDelta? ReciprocalDelta,
    [property: JsonPropertyName("errorMessage")] string? ErrorMessage);

/// <summary>
/// Metadata representing a peer region in the geo-distributed cluster.
/// </summary>
public sealed record RegionPeer(
    [property: JsonPropertyName("regionId")] string RegionId,
    [property: JsonPropertyName("endpoint")] string Endpoint,
    [property: JsonPropertyName("seatRangeStart")] int SeatRangeStart,
    [property: JsonPropertyName("seatRangeEnd")] int SeatRangeEnd,
    [property: JsonPropertyName("lastSeen")] DateTimeOffset LastSeen,
    [property: JsonPropertyName("isHealthy")] bool IsHealthy,
    [property: JsonPropertyName("latencyMs")] long LatencyMs);

/// <summary>
/// Diagnostic status view of the local region and the overall cluster.
/// </summary>
public sealed record ClusterStatusDto(
    [property: JsonPropertyName("localRegionId")] string LocalRegionId,
    [property: JsonPropertyName("localClock")] VectorClock LocalClock,
    [property: JsonPropertyName("peers")] IReadOnlyList<RegionPeer> Peers,
    [property: JsonPropertyName("localAllocatedSeats")] int LocalAllocatedSeats,
    [property: JsonPropertyName("globalAllocatedSeats")] int GlobalAllocatedSeats,
    [property: JsonPropertyName("totalDisjointCapacity")] int TotalDisjointCapacity);
