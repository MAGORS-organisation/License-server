using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Symbolon.Protocol;

public sealed record CheckoutRequestDto
{
    [Required]
    [JsonPropertyName("licenseKey")]
    public required string LicenseKey { get; init; }

    [Required]
    [MinLength(1)]
    [JsonPropertyName("fingerprintComponents")]
    public required IReadOnlyDictionary<string, string> FingerprintComponents { get; init; }

    [Range(1, 64)]
    [JsonPropertyName("quantity")]
    public int? Quantity { get; init; }

    [JsonPropertyName("features")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Features { get; init; }

    [JsonPropertyName("allowQueue")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? AllowQueue { get; init; }

    [JsonPropertyName("machineId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MachineId { get; init; }

    [JsonPropertyName("userId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UserId { get; init; }

    [JsonPropertyName("priority")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Priority { get; init; }

    public string ToFingerprintHash() => FingerprintComponents is { Count: > 0 }
        ? FingerprintHelper.ComputeHash(FingerprintComponents)
        : string.Empty;
}

public sealed record CheckoutResponseDto
{
    [JsonPropertyName("leaseId")]
    public required string LeaseId { get; init; }

    [JsonPropertyName("token")]
    public required string Token { get; init; }

    [JsonPropertyName("expiresAt")]
    public required DateTimeOffset ExpiresAt { get; init; }

    [JsonPropertyName("seat")]
    public required int Seat { get; init; }

    [JsonPropertyName("entitlements")]
    public required IReadOnlyList<string> Entitlements { get; init; }
}

public sealed record RenewRequestDto
{
    [JsonPropertyName("clientSeq")]
    public required long ClientSeq { get; init; }

    [JsonPropertyName("fingerprintComponents")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? FingerprintComponents { get; init; }

    [JsonPropertyName("fingerprintHash")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FingerprintHash { get; init; }

    public string? ResolveFingerprintHash()
    {
        if (!string.IsNullOrWhiteSpace(FingerprintHash))
        {
            return FingerprintHash;
        }

        if (FingerprintComponents is not null && FingerprintComponents.Count > 0)
        {
            return FingerprintHelper.ComputeHash(FingerprintComponents);
        }

        return null;
    }
}

public sealed record RenewResponseDto
{
    [JsonPropertyName("token")]
    public required string Token { get; init; }

    [JsonPropertyName("expiresAt")]
    public required DateTimeOffset ExpiresAt { get; init; }

    [JsonPropertyName("leaseSeq")]
    public required long LeaseSeq { get; init; }
}

public sealed record ReleaseRequestDto
{
    [JsonPropertyName("leaseId")]
    public required string LeaseId { get; init; }
}

public sealed record ReleaseResponseDto
{
    [JsonPropertyName("success")]
    public required bool Success { get; init; }
}

public sealed record BorrowRequestDto
{
    [Range(1, 30)]
    [JsonPropertyName("days")]
    public int Days { get; init; }

    [JsonPropertyName("possessionPublicKeyJwk")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PossessionPublicKeyJwk { get; init; }

    public BorrowRequestDto() { }

    public BorrowRequestDto(int days, string? possessionPublicKeyJwk = null)
    {
        Days = days;
        PossessionPublicKeyJwk = possessionPublicKeyJwk;
    }
}

public sealed record BorrowResponseDto
{
    [JsonPropertyName("leaseId")]
    public string LeaseId { get; init; } = string.Empty;

    [JsonPropertyName("borrowedUntil")]
    public DateTimeOffset BorrowedUntil { get; init; }

    [JsonPropertyName("token")]
    public string Token { get; init; } = string.Empty;

    [JsonPropertyName("symlease")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Symlease { get; init; }

    [JsonPropertyName("possessionKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PossessionKey { get; init; }

    public BorrowResponseDto() { }

    public BorrowResponseDto(string leaseId, DateTimeOffset borrowedUntil, string token, string? symlease = null, string? possessionKey = null)
    {
        LeaseId = leaseId;
        BorrowedUntil = borrowedUntil;
        Token = token;
        Symlease = symlease;
        PossessionKey = possessionKey;
    }
}

public sealed record ReturnChallengeResponseDto
{
    [JsonPropertyName("leaseId")]
    public required string LeaseId { get; init; }

    [JsonPropertyName("nonce")]
    public required string Nonce { get; init; }

    [JsonPropertyName("expiresAt")]
    public required DateTimeOffset ExpiresAt { get; init; }
}

public sealed record EarlyReturnRequestDto
{
    [Required]
    [JsonPropertyName("symlease")]
    public required string Symlease { get; init; }

    [Required]
    [JsonPropertyName("nonce")]
    public required string Nonce { get; init; }

    [Required]
    [JsonPropertyName("signature")]
    public required string Signature { get; init; }
}

public sealed record EarlyReturnResponseDto
{
    [JsonPropertyName("success")]
    public required bool Success { get; init; }

    [JsonPropertyName("returnedAt")]
    public required DateTimeOffset ReturnedAt { get; init; }

    [JsonPropertyName("leaseId")]
    public required string LeaseId { get; init; }
}

public sealed record QueuedResponseDto
{
    [JsonPropertyName("status")]
    public string Status { get; init; } = "queued";

    [JsonPropertyName("ticket")]
    public required string Ticket { get; init; }

    [JsonPropertyName("position")]
    public required int Position { get; init; }

    [JsonPropertyName("estimatedWait")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EstimatedWait { get; init; }

    [JsonPropertyName("retryAfterSeconds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? RetryAfterSeconds { get; init; }

    [JsonPropertyName("priority")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Priority { get; init; }
}

public sealed record QueueStatusResponseDto
{
    [JsonPropertyName("ticket")]
    public required string Ticket { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; } // "waiting" | "ready" | "cancelled" | "expired"

    [JsonPropertyName("position")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Position { get; init; }

    [JsonPropertyName("priority")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Priority { get; init; }

    [JsonPropertyName("estimatedWait")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EstimatedWait { get; init; }

    [JsonPropertyName("retryAfterSeconds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? RetryAfterSeconds { get; init; }

    [JsonPropertyName("leaseId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LeaseId { get; init; }

    [JsonPropertyName("token")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Token { get; init; }

    [JsonPropertyName("seat")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? Seat { get; init; }

    [JsonPropertyName("expiresAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ExpiresAt { get; init; }
}

public sealed record QueueTicketItemDto
{
    [JsonPropertyName("ticket")]
    public required string Ticket { get; init; }

    [JsonPropertyName("licenseId")]
    public required string LicenseId { get; init; }

    [JsonPropertyName("fingerprint")]
    public required string Fingerprint { get; init; }

    [JsonPropertyName("machineId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MachineId { get; init; }

    [JsonPropertyName("userId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? UserId { get; init; }

    [JsonPropertyName("quantity")]
    public int Quantity { get; init; } = 1;

    [JsonPropertyName("priority")]
    public int Priority { get; init; }

    [JsonPropertyName("status")]
    public required string Status { get; init; }

    [JsonPropertyName("position")]
    public int Position { get; init; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; init; }

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset ExpiresAt { get; init; }
}

public sealed record ConsumeQuotaRequestDto
{
    [Required]
    [JsonPropertyName("licenseKey")]
    public required string LicenseKey { get; init; }

    [Required]
    [JsonPropertyName("entitlementCode")]
    public required string EntitlementCode { get; init; }

    [Range(1, 10000000)]
    [JsonPropertyName("units")]
    public long Units { get; init; } = 1;
}

public sealed record QuotaBalanceDto(
    [property: JsonPropertyName("entitlementCode")] string EntitlementCode,
    [property: JsonPropertyName("totalUnits")] long TotalUnits,
    [property: JsonPropertyName("consumedUnits")] long ConsumedUnits,
    [property: JsonPropertyName("remainingUnits")] long RemainingUnits,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt);

public sealed record TransparencyProofStepDto(
    [property: JsonPropertyName("hash")] string Hash,
    [property: JsonPropertyName("direction")] string Direction);

public sealed record TransparencyRootResponseDto(
    [property: JsonPropertyName("rootHash")] string RootHash,
    [property: JsonPropertyName("treeSize")] int TreeSize,
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp);

public sealed record TransparencyInclusionResponseDto(
    [property: JsonPropertyName("auditId")] string AuditId,
    [property: JsonPropertyName("leafIndex")] int LeafIndex,
    [property: JsonPropertyName("treeSize")] int TreeSize,
    [property: JsonPropertyName("leafHash")] string LeafHash,
    [property: JsonPropertyName("rootHash")] string RootHash,
    [property: JsonPropertyName("path")] IReadOnlyList<TransparencyProofStepDto> Path);

public sealed record VerifyTransparencyProofRequestDto
{
    [Required]
    [JsonPropertyName("leafHash")]
    public required string LeafHash { get; init; }

    [Required]
    [JsonPropertyName("rootHash")]
    public required string RootHash { get; init; }

    [Required]
    [JsonPropertyName("path")]
    public required IReadOnlyList<TransparencyProofStepDto> Path { get; init; }
}

public sealed record VerifyTransparencyProofResponseDto(
    [property: JsonPropertyName("isValid")] bool IsValid,
    [property: JsonPropertyName("message")] string Message);

public sealed record SignedTreeHeadDto(
    [property: JsonPropertyName("treeSize")] int TreeSize,
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp,
    [property: JsonPropertyName("rootHash")] string RootHash,
    [property: JsonPropertyName("signature")] string Signature,
    [property: JsonPropertyName("keyId")] string KeyId,
    [property: JsonPropertyName("algorithm")] string Algorithm);

public sealed record TransparencyConsistencyResponseDto(
    [property: JsonPropertyName("oldSize")] int OldSize,
    [property: JsonPropertyName("newSize")] int NewSize,
    [property: JsonPropertyName("oldRoot")] string OldRoot,
    [property: JsonPropertyName("newRoot")] string NewRoot,
    [property: JsonPropertyName("proof")] IReadOnlyList<string> Proof);

public sealed record VerifyTransparencyConsistencyRequestDto
{
    [Required]
    [JsonPropertyName("oldSize")]
    public required int OldSize { get; init; }

    [Required]
    [JsonPropertyName("newSize")]
    public required int NewSize { get; init; }

    [Required]
    [JsonPropertyName("oldRoot")]
    public required string OldRoot { get; init; }

    [Required]
    [JsonPropertyName("newRoot")]
    public required string NewRoot { get; init; }

    [Required]
    [JsonPropertyName("proof")]
    public required IReadOnlyList<string> Proof { get; init; }
}

public sealed record VerifyTransparencyConsistencyResponseDto(
    [property: JsonPropertyName("isConsistent")] bool IsConsistent,
    [property: JsonPropertyName("message")] string Message);

public sealed record CraComplianceReportDto(
    [property: JsonPropertyName("productName")] string ProductName,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("manufacturer")] string Manufacturer,
    [property: JsonPropertyName("complianceStatus")] string ComplianceStatus,
    [property: JsonPropertyName("standards")] IReadOnlyList<string> Standards,
    [property: JsonPropertyName("vulnerabilityReportingUrl")] Uri VulnerabilityReportingUri,
    [property: JsonPropertyName("securityContact")] string SecurityContact,
    [property: JsonPropertyName("patchSupportUntil")] DateTimeOffset PatchSupportUntil,
    [property: JsonPropertyName("sbomEndpoint")] string SbomEndpoint,
    [property: JsonPropertyName("generatedAt")] DateTimeOffset GeneratedAt);

public sealed record SbomComponentDto(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("purl")] string Purl,
    [property: JsonPropertyName("licenses")] IReadOnlyList<string> Licenses,
    [property: JsonPropertyName("hashes")] IReadOnlyDictionary<string, string> Hashes);

public sealed record CycloneDxSbomDto(
    [property: JsonPropertyName("bomFormat")] string BomFormat,
    [property: JsonPropertyName("specVersion")] string SpecVersion,
    [property: JsonPropertyName("serialNumber")] string SerialNumber,
    [property: JsonPropertyName("version")] int Version,
    [property: JsonPropertyName("metadata")] object Metadata,
    [property: JsonPropertyName("components")] IReadOnlyList<SbomComponentDto> Components);

public sealed record ServerTelemetryDto(
    [property: JsonPropertyName("cpuPercent")] double CpuPercent,
    [property: JsonPropertyName("memoryMb")] double MemoryMb,
    [property: JsonPropertyName("diskMb")] double DiskMb,
    [property: JsonPropertyName("networkActivity")] string NetworkActivity,
    [property: JsonPropertyName("serverIp")] string ServerIp,
    [property: JsonPropertyName("serverPort")] int ServerPort,
    [property: JsonPropertyName("currentUser")] string CurrentUser,
    [property: JsonPropertyName("uptimeSeconds")] long UptimeSeconds,
    [property: JsonPropertyName("activeSeats")] long ActiveSeats);

public sealed record AcquireFeatureRequestDto
{
    [Required]
    [JsonPropertyName("featureCode")]
    public required string FeatureCode { get; init; }

    [JsonPropertyName("version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Version { get; init; }

    [JsonPropertyName("ttlSeconds")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TtlSeconds { get; init; }
}

public sealed record FeatureAcquisitionResponseDto
{
    [JsonPropertyName("success")]
    public required bool Success { get; init; }

    [JsonPropertyName("featureCode")]
    public required string FeatureCode { get; init; }

    [JsonPropertyName("version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Version { get; init; }

    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; init; }

    [JsonPropertyName("inUse")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? InUse { get; init; }

    [JsonPropertyName("maxSeats")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxSeats { get; init; }
}

public sealed record ReleaseFeatureRequestDto
{
    [Required]
    [JsonPropertyName("featureCode")]
    public required string FeatureCode { get; init; }
}

public sealed record ReleaseFeatureResponseDto
{
    [JsonPropertyName("success")]
    public required bool Success { get; init; }

    [JsonPropertyName("featureCode")]
    public required string FeatureCode { get; init; }

    [JsonPropertyName("reason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Reason { get; init; }
}

public sealed record ActiveFeatureInfoDto
{
    [JsonPropertyName("featureCode")]
    public required string FeatureCode { get; init; }

    [JsonPropertyName("version")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Version { get; init; }

    [JsonPropertyName("acquiredAt")]
    public required DateTimeOffset AcquiredAt { get; init; }

    [JsonPropertyName("expiresAt")]
    public required DateTimeOffset ExpiresAt { get; init; }
}

public static class ProblemTypes
{
    public const string LicenseNotFound = "https://symbolon.dev/errors/license-not-found";
    public const string PoolExhausted = "https://symbolon.dev/errors/pool-exhausted";
    public const string LeaseUnknown = "https://symbolon.dev/errors/lease-unknown";
    public const string SeatReassigned = "https://symbolon.dev/errors/seat-reassigned";
    public const string StaleSequence = "https://symbolon.dev/errors/stale-sequence";
    public const string FingerprintMismatch = "https://symbolon.dev/errors/fingerprint-mismatch";
    public const string InvalidRequest = "https://symbolon.dev/errors/invalid-request";
    public const string UserNotAuthorized = "https://symbolon.dev/errors/user-not-authorized";
    public const string QuotaExhausted = "https://symbolon.dev/errors/quota-exhausted";
    public const string FeatureDenied = "https://symbolon.dev/errors/feature-denied";
    public const string FeatureCapacityExceeded = "https://symbolon.dev/errors/feature-capacity-exceeded";
    public const string Unauthorized = "https://symbolon.dev/errors/unauthorized";
    public const string Forbidden = "https://symbolon.dev/errors/forbidden";
    public const string NotFound = "https://symbolon.dev/errors/not-found";
    public const string QueueNotFound = "https://symbolon.dev/errors/queue-not-found";
    public const string RuleDenied = "https://symbolon.dev/problems/rule-denied";
    public const string GroupQuotaExceeded = "https://symbolon.dev/problems/group-quota-exceeded";
    public const string BorrowDisabled = "https://symbolon.dev/problems/borrow-disabled";
    public const string BorrowDurationExceeded = "https://symbolon.dev/problems/borrow-duration-exceeded";
    public const string BorrowLimitExceeded = "https://symbolon.dev/problems/borrow-limit-exceeded";
    public const string ProofOfPossessionRequired = "https://symbolon.dev/problems/proof-of-possession-required";
    public const string InvalidProofOfPossession = "https://symbolon.dev/problems/invalid-proof-of-possession";
    public const string ChallengeExpired = "https://symbolon.dev/problems/challenge-expired";
}

public sealed record VerifyFingerprintMatchRequestDto
{
    [JsonPropertyName("storedComponents")]
    public required Dictionary<string, string> StoredComponents { get; init; }

    [JsonPropertyName("incomingComponents")]
    public required Dictionary<string, string> IncomingComponents { get; init; }

    [JsonPropertyName("strategy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Strategy { get; init; }
}

public sealed record VerifyFingerprintMatchResponseDto
{
    [JsonPropertyName("isMatch")]
    public required bool IsMatch { get; init; }

    [JsonPropertyName("strategyUsed")]
    public required string StrategyUsed { get; init; }

    [JsonPropertyName("commonComponentsCount")]
    public required int CommonComponentsCount { get; init; }

    [JsonPropertyName("matchedComponentsCount")]
    public required int MatchedComponentsCount { get; init; }

    [JsonPropertyName("matchedKeys")]
    public required IReadOnlyList<string> MatchedKeys { get; init; }

    [JsonPropertyName("mismatchedKeys")]
    public required IReadOnlyList<string> MismatchedKeys { get; init; }

    [JsonPropertyName("failureReason")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? FailureReason { get; init; }

    [JsonPropertyName("matchRatio")]
    public double MatchRatio { get; init; }
}

public sealed record ActivationRequestDto
{
    [Required]
    [JsonPropertyName("licenseKey")]
    public required string LicenseKey { get; init; }

    [Required]
    [JsonPropertyName("fingerprintComponents")]
    public required Dictionary<string, string> FingerprintComponents { get; init; }

    [JsonPropertyName("machineId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MachineId { get; init; }
}

public sealed record ActivationResponseDto
{
    [JsonPropertyName("activationId")]
    public required string ActivationId { get; init; }

    [JsonPropertyName("licenseId")]
    public required string LicenseId { get; init; }

    [JsonPropertyName("fingerprint")]
    public required string Fingerprint { get; init; }

    [JsonPropertyName("state")]
    public required string State { get; init; }

    [JsonPropertyName("activatedAt")]
    public required DateTimeOffset ActivatedAt { get; init; }
}

public sealed record DeactivateResponseDto
{
    [JsonPropertyName("success")]
    public required bool Success { get; init; }

    [JsonPropertyName("activationId")]
    public required string ActivationId { get; init; }
}

public sealed record BillingWebhookResponseDto(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("licenseId")] string? LicenseId,
    [property: JsonPropertyName("licenseKey")] string? LicenseKey,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("customerId")] string? CustomerId = null,
    [property: JsonPropertyName("subscriptionId")] string? SubscriptionId = null);

public sealed record ReserveTokensRequestDto
{
    [JsonPropertyName("walletId")]
    public required string WalletId { get; init; }

    [JsonPropertyName("featureCode")]
    public required string FeatureCode { get; init; }

    [JsonPropertyName("estimatedUnits")]
    public required decimal EstimatedUnits { get; init; }

    [JsonPropertyName("isDurationMinutes")]
    public bool IsDurationMinutes { get; init; }

    [JsonPropertyName("machineId")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MachineId { get; init; }

    [JsonPropertyName("clientRef")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClientRef { get; init; }

    [JsonPropertyName("idempotencyKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IdempotencyKey { get; init; }

    [JsonPropertyName("reservationTtl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public TimeSpan? ReservationTtl { get; init; }
}

public sealed record ReserveTokensResponseDto
{
    [JsonPropertyName("success")]
    public required bool Success { get; init; }

    [JsonPropertyName("reservationId")]
    public string? ReservationId { get; init; }

    [JsonPropertyName("reservedAmount")]
    public decimal ReservedAmount { get; init; }

    [JsonPropertyName("availableBalance")]
    public decimal AvailableBalance { get; init; }

    [JsonPropertyName("overdraftRemaining")]
    public decimal OverdraftRemaining { get; init; }

    [JsonPropertyName("failureReason")]
    public string? FailureReason { get; init; }
}

public sealed record HeartbeatTokensRequestDto
{
    [JsonPropertyName("reservationId")]
    public required string ReservationId { get; init; }

    [JsonPropertyName("deltaUnits")]
    public required decimal DeltaUnits { get; init; }

    [JsonPropertyName("isDurationMinutes")]
    public bool IsDurationMinutes { get; init; }

    [JsonPropertyName("idempotencyKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IdempotencyKey { get; init; }
}

public sealed record HeartbeatTokensResponseDto
{
    [JsonPropertyName("success")]
    public required bool Success { get; init; }

    [JsonPropertyName("totalConsumed")]
    public decimal TotalConsumed { get; init; }

    [JsonPropertyName("remainingReserved")]
    public decimal RemainingReserved { get; init; }

    [JsonPropertyName("availableBalance")]
    public decimal AvailableBalance { get; init; }

    [JsonPropertyName("failureReason")]
    public string? FailureReason { get; init; }
}

public sealed record CommitTokensRequestDto
{
    [JsonPropertyName("reservationId")]
    public required string ReservationId { get; init; }

    [JsonPropertyName("actualUnits")]
    public required decimal ActualUnits { get; init; }

    [JsonPropertyName("isDurationMinutes")]
    public bool IsDurationMinutes { get; init; }

    [JsonPropertyName("idempotencyKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IdempotencyKey { get; init; }
}

public sealed record CommitTokensResponseDto
{
    [JsonPropertyName("success")]
    public required bool Success { get; init; }

    [JsonPropertyName("consumedCredits")]
    public decimal ConsumedCredits { get; init; }

    [JsonPropertyName("refundedCredits")]
    public decimal RefundedCredits { get; init; }

    [JsonPropertyName("newBalance")]
    public decimal NewBalance { get; init; }

    [JsonPropertyName("failureReason")]
    public string? FailureReason { get; init; }
}

public sealed record RollbackTokensRequestDto
{
    [JsonPropertyName("reservationId")]
    public required string ReservationId { get; init; }

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = "Operation aborted";

    [JsonPropertyName("idempotencyKey")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IdempotencyKey { get; init; }
}

public sealed record RollbackTokensResponseDto
{
    [JsonPropertyName("success")]
    public required bool Success { get; init; }

    [JsonPropertyName("restoredCredits")]
    public decimal RestoredCredits { get; init; }

    [JsonPropertyName("newBalance")]
    public decimal NewBalance { get; init; }

    [JsonPropertyName("failureReason")]
    public string? FailureReason { get; init; }
}

public sealed record TokenWalletBalanceResponseDto
{
    [JsonPropertyName("walletId")]
    public required string WalletId { get; init; }

    [JsonPropertyName("walletCode")]
    public required string WalletCode { get; init; }

    [JsonPropertyName("walletName")]
    public required string WalletName { get; init; }

    [JsonPropertyName("totalCredits")]
    public required decimal TotalCredits { get; init; }

    [JsonPropertyName("balance")]
    public required decimal Balance { get; init; }

    [JsonPropertyName("reservedCredits")]
    public required decimal ReservedCredits { get; init; }

    [JsonPropertyName("availableBalance")]
    public required decimal AvailableBalance { get; init; }

    [JsonPropertyName("overdraftLimit")]
    public required decimal OverdraftLimit { get; init; }

    [JsonPropertyName("state")]
    public required string State { get; init; }

    [JsonPropertyName("expiresAt")]
    public DateTimeOffset? ExpiresAt { get; init; }

    [JsonPropertyName("isLowBalance")]
    public required bool IsLowBalance { get; init; }
}

public sealed record AgentStatusDto(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("licenseKey")] string? LicenseKey,
    [property: JsonPropertyName("leaseId")] string? LeaseId,
    [property: JsonPropertyName("seatNo")] int? SeatNo,
    [property: JsonPropertyName("expiresAt")] DateTimeOffset? ExpiresAt,
    [property: JsonPropertyName("machineId")] string MachineId,
    [property: JsonPropertyName("offlineAllowed")] bool OfflineAllowed,
    [property: JsonPropertyName("lastError")] string? LastError,
    [property: JsonPropertyName("updatedAt")] DateTimeOffset UpdatedAt
);

public sealed record AgentActionResponseDto(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("leaseId")] string? LeaseId = null
);



