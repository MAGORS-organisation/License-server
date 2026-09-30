namespace Symbolon.Domain.Grants;

public sealed record SeatGrantRecord(
    string Id,
    string LicenseId,
    string RelayId,
    int Seats,
    int SeatFrom,
    int SeatTo,
    long Seq,
    long? Supersedes,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter,
    DateTimeOffset? RevokedAt,
    string Document);

public sealed record AirGapNonceRecord(
    string Nonce,
    string RelayId,
    DateTimeOffset SeenAt,
    DateTimeOffset ExpiresAt);

public interface ISeatGrantStore
{
    Task<IReadOnlyList<SeatGrantRecord>> GetActiveGrantsForLicenseAsync(
        string licenseId,
        DateTimeOffset now,
        CancellationToken ct = default);

    Task<IReadOnlyList<SeatGrantRecord>> GetGrantsForLicenseAsync(
        string licenseId,
        CancellationToken ct = default);

    Task<IReadOnlyList<SeatGrantRecord>> GetAllGrantsAsync(
        CancellationToken ct = default);

    Task<SeatGrantRecord?> GetGrantByIdAsync(
        string grantId,
        CancellationToken ct = default);

    Task<long> GetHighestSeqAsync(
        string licenseId,
        string relayId,
        CancellationToken ct = default);

    Task<bool> HasNonceBeenSeenAsync(
        string nonce,
        CancellationToken ct = default);

    Task RecordNonceAsync(
        string nonce,
        string relayId,
        DateTimeOffset seenAt,
        DateTimeOffset expiresAt,
        CancellationToken ct = default);

    Task<SeatGrantRecord> SaveGrantAsync(
        SeatGrantRecord grant,
        CancellationToken ct = default);

    Task<bool> RevokeGrantAsync(
        string grantId,
        DateTimeOffset revokedAt,
        CancellationToken ct = default);
}
