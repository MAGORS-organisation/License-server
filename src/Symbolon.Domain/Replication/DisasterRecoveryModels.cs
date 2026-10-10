namespace Symbolon.Domain.Replication;

public sealed record DisasterRecoveryReport(
    string PromotedRegionId,
    string FailedRegionId,
    int ReclaimedSeatsCount,
    int NewTotalCapacity,
    long VectorClockAdvancedBy,
    DateTimeOffset FailoverTimestamp,
    bool QuorumMaintained,
    string StatusMessage
);

public sealed record DisasterRecoveryFailoverRequest(
    string TargetRegion,
    string FailedRegion,
    bool RebalanceSeats = true
);
