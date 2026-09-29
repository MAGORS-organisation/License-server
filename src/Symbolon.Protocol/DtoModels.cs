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

    public string ToFingerprintHash() => FingerprintHelper.ComputeHash(FingerprintComponents);
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

    public BorrowRequestDto() { }

    public BorrowRequestDto(int days)
    {
        Days = days;
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

    public BorrowResponseDto() { }

    public BorrowResponseDto(string leaseId, DateTimeOffset borrowedUntil, string token)
    {
        LeaseId = leaseId;
        BorrowedUntil = borrowedUntil;
        Token = token;
    }
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
}

