namespace Symbolon.Domain;

public enum RenewOutcomeType
{
    Renewed,
    Resurrected,
    Unknown,
    Taken,
    SeqReplay,
    Conflict
}

public sealed record RenewOutcome(RenewOutcomeType Type, SeatAllocation? Allocation = null)
{
    public static RenewOutcome Renewed(SeatAllocation allocation) => new(RenewOutcomeType.Renewed, allocation);
    public static RenewOutcome Resurrected(SeatAllocation allocation) => new(RenewOutcomeType.Resurrected, allocation);
    public static RenewOutcome Unknown => new(RenewOutcomeType.Unknown);
    public static RenewOutcome Taken => new(RenewOutcomeType.Taken);
    public static RenewOutcome SeqReplay => new(RenewOutcomeType.SeqReplay);
    public static RenewOutcome Conflict => new(RenewOutcomeType.Conflict);
}

/// <summary>
/// Persistence contract for floating seat allocations. Implementations exist for PostgreSQL (control plane) and SQLite (relay).
/// </summary>
public interface ISeatStore
{
    Task<SeatAllocation?> TryAcquireOneAsync(
        string licenseId,
        string fingerprint,
        string? machineId,
        DateTimeOffset now,
        TimeSpan ttl,
        string? reservationTarget = null,
        CancellationToken ct = default);

    Task<SeatAllocation[]?> TryAcquireManyAsync(
        string licenseId,
        string fingerprint,
        string? machineId,
        int quantity,
        DateTimeOffset now,
        TimeSpan ttl,
        string? reservationTarget = null,
        CancellationToken ct = default);

    Task SyncSeatReservationsAsync(
        string licenseId,
        IReadOnlyList<(string Target, int Count)> reservations,
        CancellationToken ct = default);

    Task<RenewOutcome> TryRenewAsync(
        string leaseId,
        string fingerprint,
        long clientSeq,
        DateTimeOffset now,
        TimeSpan ttl,
        TimeSpan resurrectionWindow,
        CancellationToken ct = default);

    Task<bool> TryReleaseAsync(string leaseId, DateTimeOffset now, CancellationToken ct = default);

    Task<bool> TryBorrowSeatAsync(
        string leaseId,
        DateTimeOffset borrowedUntil,
        string possessionKeyJwk,
        CancellationToken ct = default);

    Task<bool> TryReturnBorrowedSeatAsync(
        string leaseId,
        DateTimeOffset now,
        CancellationToken ct = default);

    Task<int> GetActiveBorrowedCountAsync(
        string licenseId,
        DateTimeOffset now,
        CancellationToken ct = default);

    Task<SeatAllocation[]?> TryGetIdempotentAsync(
        string licenseId,
        string idempotencyKey,
        DateTimeOffset now,
        CancellationToken ct = default);

    Task SaveIdempotentAsync(
        string licenseId,
        string idempotencyKey,
        SeatAllocation[] allocations,
        DateTimeOffset now,
        TimeSpan ttl,
        CancellationToken ct = default);

    Task<TimeSpan?> EstimateWaitAsync(string licenseId, DateTimeOffset now, CancellationToken ct = default);
}
