namespace Symbolon.Domain;

/// <summary>
/// State of a single material seat row in the floating seat store.
/// </summary>
public sealed record SeatAllocation
{
    public required string SeatId { get; init; }
    public required int SeatNo { get; init; }
    public required string LicenseId { get; init; }
    public string? LeaseId { get; init; }
    public string? HolderFingerprint { get; init; }
    public string? MachineId { get; init; }
    public DateTimeOffset? AcquiredAt { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public long LeaseSeq { get; init; }
    public bool IsOverage { get; init; }
}
