namespace Symbolon.Domain.Tokens;

public sealed record TokenWalletModel(
    string Id,
    string TenantId,
    string? LicenseId,
    string Code,
    string Name,
    decimal TotalCredits,
    decimal Balance,
    decimal ReservedCredits,
    decimal OverdraftLimit,
    string State,
    DateTimeOffset? ExpiresAt,
    decimal? ThresholdLowAlert,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastRefillAt);

public sealed record TokenRateModel(
    string Id,
    string TenantId,
    string? ProductId,
    string FeatureCode,
    decimal RatePerMinute,
    decimal RatePerUnit,
    string Description,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TokenReservationModel(
    string Id,
    string WalletId,
    string FeatureCode,
    decimal ReservedAmount,
    decimal ConsumedAmount,
    string? MachineId,
    string? ClientRef,
    string Status,
    string? IdempotencyKey,
    DateTimeOffset ExpiresAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record TokenLedgerEntryModel(
    long Id,
    string WalletId,
    string? ReservationId,
    string TransactionType,
    decimal Amount,
    decimal BalanceAfter,
    string? FeatureCode,
    string? IdempotencyKey,
    DateTimeOffset Timestamp,
    string MetadataJson);

public sealed record CreateWalletRequest(
    string TenantId,
    string Code,
    string Name,
    string? LicenseId = null,
    decimal InitialCredits = 0,
    decimal OverdraftLimit = 0,
    decimal? ThresholdLowAlert = null,
    DateTimeOffset? ExpiresAt = null);

public sealed record CreditWalletRequest(
    string WalletId,
    decimal Amount,
    string Reason,
    string? IdempotencyKey = null);

public sealed record ReserveTokensRequest(
    string WalletId,
    string FeatureCode,
    decimal EstimatedUnits,
    bool IsDurationMinutes = false,
    string? MachineId = null,
    string? ClientRef = null,
    string? IdempotencyKey = null,
    TimeSpan? ReservationTtl = null);

public sealed record ReserveTokensResult(
    bool Success,
    string? ReservationId,
    decimal ReservedAmount,
    decimal AvailableBalance,
    decimal OverdraftRemaining,
    string? FailureReason = null);

public sealed record HeartbeatTokensRequest(
    string ReservationId,
    decimal DeltaUnits,
    bool IsDurationMinutes = false,
    string? IdempotencyKey = null);

public sealed record HeartbeatTokensResult(
    bool Success,
    decimal TotalConsumed,
    decimal RemainingReserved,
    decimal AvailableBalance,
    string? FailureReason = null);

public sealed record CommitTokensRequest(
    string ReservationId,
    decimal ActualTotalUnits,
    bool IsDurationMinutes = false,
    string? IdempotencyKey = null);

public sealed record CommitTokensResult(
    bool Success,
    decimal ConsumedCredits,
    decimal ReleasedCredits,
    decimal FinalBalance,
    bool IsLowBalanceAlert,
    string? FailureReason = null);

public sealed record RollbackTokensRequest(
    string ReservationId,
    string? Reason = null,
    string? IdempotencyKey = null);

public sealed record RollbackTokensResult(
    bool Success,
    decimal ReleasedCredits,
    decimal CurrentBalance,
    string? FailureReason = null);

public sealed record SetRateRequest(
    string TenantId,
    string FeatureCode,
    decimal RatePerMinute,
    decimal RatePerUnit,
    string Description,
    string? ProductId = null);

public sealed record TokenWalletBalanceDto(
    string WalletId,
    string Code,
    string Name,
    decimal Balance,
    decimal ReservedCredits,
    decimal OverdraftLimit,
    decimal AvailableCredits,
    string State,
    bool IsLowBalance);
