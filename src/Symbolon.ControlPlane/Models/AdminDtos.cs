using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Symbolon.Crypto;
using Symbolon.Domain.Experiments;

namespace Symbolon.ControlPlane.Models;

public sealed record CreateTenantDto(
    [Required, MaxLength(64)] string Slug,
    [Required, MaxLength(256)] string Name);

public sealed record TenantDto(
    string Id,
    string Slug,
    string Name,
    DateTimeOffset CreatedAt);

public sealed record CreateProductDto(
    [Required, MaxLength(64)] string Code,
    [Required, MaxLength(256)] string Name,
    IReadOnlyList<string>? Platforms);

public sealed record ProductDto(
    string Id,
    string TenantId,
    string Code,
    string Name,
    IReadOnlyList<string> Platforms,
    DateTimeOffset CreatedAt);

public sealed record CreatePolicyDto
{
    [Required] public required string ProductId { get; init; }
    [Required, MaxLength(64)] public required string Code { get; init; }
    [Required, MaxLength(256)] public required string Name { get; init; }
    public string LicenseModel { get; init; } = "floating";
    public string? Duration { get; init; } = "P1Y";
    public int MaxSeats { get; init; } = 1;
    public string SeatUnit { get; init; } = "machine";
    public string OverageStrategy { get; init; } = "no-overage";
    public string ExpirationStrategy { get; init; } = "restrict";
    public int LeaseTtlSeconds { get; init; } = 600;
    public int HeartbeatIntervalSeconds { get; init; } = 120;
    public int GraceTtlSeconds { get; init; } = 14400;
    public int ResurrectionWindowSeconds { get; init; } = 300;
    public bool BorrowEnabled { get; init; }
    public int BorrowMaxDurationDays { get; init; } = 7;
    public int BorrowMaxConcurrent { get; init; } = 5;
    public bool OfflineAllowed { get; init; } = true;
    public string CryptoProfile { get; init; } = "hybrid-v1";
    public string MachineMatching { get; init; } = "match-most";
    public string MachineUniqueness { get; init; } = "per-license";
    public IReadOnlyList<string>? Entitlements { get; init; }
}

public sealed record PolicyDto(
    string Id,
    string TenantId,
    string ProductId,
    string Code,
    string Name,
    string LicenseModel,
    int MaxSeats,
    string SeatUnit,
    int LeaseTtlSeconds,
    string MachineMatching = "match-most",
    string MachineUniqueness = "per-license");

public sealed record CreateLicenseDto
{
    [Required] public required string PolicyId { get; init; }
    public string? CustomerRef { get; init; }
    public int? MaxSeats { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public string? KeyPrefix { get; init; } = "SYM";
}

public sealed record LicenseResponseDto(
    string Id,
    string? LicenseKey,
    string TenantId,
    string PolicyId,
    string? CustomerRef,
    string State,
    int MaxSeats,
    DateTimeOffset IssuedAt,
    DateTimeOffset? ExpiresAt);

public sealed record RevokeLicenseDto(
    [Required] string Reason);

public sealed record MachineActivationAdminDto(
    string Id,
    string LicenseId,
    string Fingerprint,
    string? MachineId,
    string State,
    DateTimeOffset FirstSeen,
    DateTimeOffset LastHeartbeat,
    IReadOnlyDictionary<string, string>? Components);

public sealed record BorrowedSeatAdminDto(
    string LeaseId,
    string LicenseId,
    int SeatNo,
    string? MachineId,
    DateTimeOffset BorrowedUntil,
    double HoursRemaining);

public sealed record FraudRadarAdminDto(
    string Id,
    string LicenseId,
    string? UserId,
    string? MachineId,
    string? IpAddress,
    string RiskType,
    string RiskLevel,
    string Description,
    double? VelocityKmH,
    double? DistanceKm,
    DateTimeOffset Timestamp);

public sealed record RegisterRelayDto(
    [Required] string Name,
    string? MtlsThumbprint);

public sealed record RegisterRelayResponseDto(
    string RelayId,
    string ApiKey);

public sealed record RequestSeatGrantDto(
    [Required] string RelayId,
    [Required] string LicenseId,
    [Range(1, 1000)] int Seats,
    [Range(1, 365)] int DurationDays = 30);

public sealed record SeatGrantResponseDto(
    string GrantId,
    string Token,
    int SeatFrom,
    int SeatTo,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter);

public sealed record RelayUsageBatchDto(
    [Required] string RelayId,
    [Required] IReadOnlyList<RelayAuditEventDto> Events);

public sealed record RelayAuditEventDto(
    string Type,
    string LicenseId,
    string? Fingerprint,
    DateTimeOffset Timestamp,
    string? Detail);

public sealed record ConcurrencyReportDto(
    int TotalSeats,
    int ActiveLeases,
    int AvailableSeats,
    double UtilizationPercentage,
    int DenialsCount);

public sealed record RotateKeyDto(
    string? TenantId,
    string? Alg);

public sealed record RevokeKeyDto(
    [Required] string Reason);

public sealed record SigningKeyDto(
    string Id,
    string TenantId,
    string Kid,
    string Alg,
    string Role,
    string State,
    DateTimeOffset NotBefore,
    DateTimeOffset NotAfter);

#pragma warning disable CA1054, CA1056 // URI-like properties and parameters should not be strings (DTO mapping)
public sealed record CreateWebhookDto(
    [Required] string Url,
    IReadOnlyList<string>? Events,
    string? Secret,
    string? Name = null,
    string? Format = null);

public sealed record WebhookSubscriptionDto(
    string Id,
    string TenantId,
    string Url,
    IReadOnlyList<string> Events,
    bool IsActive,
    DateTimeOffset CreatedAt,
    string? Name = null,
    string? Format = null,
    int FailureCount = 0,
    DateTimeOffset? LastDeliveredAt = null);
#pragma warning restore CA1054, CA1056

public sealed record WebhookDeliveryDto(
    string Id,
    string SubscriptionId,
    string EventType,
    string Status,
    int? StatusCode,
    int Attempts,
    DateTimeOffset? DeliveredAt,
    string? LastError,
    DateTimeOffset CreatedAt,
    long DurationMs = 0);

public sealed record WebhookTestResultDto(
    bool Success,
    int? StatusCode,
    string? ResponseBody,
    long ElapsedMilliseconds,
    string? Error);

public sealed record AssignLicenseUserDto(
    [Required] string UserId,
    string? GroupName);

public sealed record LicenseUserDto(
    string Id,
    string LicenseId,
    string UserId,
    string? GroupName,
    DateTimeOffset CreatedAt);

public sealed record SetLicenseQuotaDto(
    [Required] string EntitlementCode,
    [Range(1, 1000000000)] long TotalUnits);

public sealed record LicenseQuotaAdminDto(
    string Id,
    string LicenseId,
    string EntitlementCode,
    long TotalUnits,
    long ConsumedUnits,
    long RemainingUnits,
    DateTimeOffset UpdatedAt);

public sealed record CreateApiKeyDto(
    [Required, MaxLength(256)] string Name,
    string Role = "admin:super",
    DateTimeOffset? ExpiresAt = null);

public sealed record ApiKeyResponseDto(
    string Id,
    string TenantId,
    string Name,
    string Prefix,
    string Role,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset CreatedAt,
    string? SecretKey = null);

public sealed record CreateFeatureDto(
    [Required, MaxLength(64)] string Code,
    [Required, MaxLength(128)] string Name,
    string? ProductId = null,
    string? Description = null,
    string? MinVersion = null,
    string? MaxVersion = null,
    bool IsFloating = true,
    int? DefaultMaxSeats = null);

public sealed record CreatePackageSuiteDto(
    [Required, MaxLength(64)] string Code,
    [Required, MaxLength(128)] string Name,
    [Required] IReadOnlyList<string> FeatureCodes,
    string? ProductId = null,
    string? Description = null);

public sealed record SetLicenseEntitlementDto(
    [Required, MaxLength(64)] string FeatureCode,
    int? MaxSeats = null,
    string? AllowedVersionRange = null,
    bool IsEnabled = true,
    string? ParametersJson = null);

public sealed record CreateExperimentDto
{
    [Required, MaxLength(64)] public required string Id { get; init; }
    [Required, MaxLength(256)] public required string Name { get; init; }
    public string? Description { get; init; }
    public int TrafficAllocation { get; init; } = 100;
    public ExperimentTargeting? Targeting { get; init; }
    public IReadOnlyList<ExperimentVariant>? Variants { get; init; }
    public ExperimentCircuitBreaker? CircuitBreaker { get; init; }
}

public sealed record UpdateExperimentDto
{
    [Required, MaxLength(256)] public required string Name { get; init; }
    public string? Description { get; init; }
    public int TrafficAllocation { get; init; } = 100;
    public ExperimentTargeting? Targeting { get; init; }
    public IReadOnlyList<ExperimentVariant>? Variants { get; init; }
    public ExperimentCircuitBreaker? CircuitBreaker { get; init; }
}

public sealed record ExperimentDto(
    string Id,
    string? TenantId,
    string Name,
    string? Description,
    string Status,
    string Salt,
    int TrafficAllocation,
    ExperimentTargeting Targeting,
    IReadOnlyList<ExperimentVariant> Variants,
    ExperimentCircuitBreaker CircuitBreaker,
    string? PromotedVariantId,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? EndedAt);

public sealed record SimulateExperimentDto(
    int ClientCount = 1000,
    string? TenantId = null,
    string? SdkLanguage = null,
    string? OsPlatform = null);

public sealed record SimulateExperimentResultDto(
    string ExperimentId,
    int TotalSimulated,
    int TotalInExperiment,
    int TotalBaseline,
    IReadOnlyDictionary<string, int> VariantCounts);

// ==========================================
// Post-Quantum Cryptography & Scanner DTOs (M7, §13.5)
// ==========================================
public sealed record SetPqcProfileDto(
    [Required] string Profile);

public sealed record GeneratePqcKeyDto(
    [Required] string Alg,
    [Required] string Kid);

public sealed record PqcEncryptRequestDto(
    [Required] string RecipientKid,
    [Required] string PlaintextBase64);

public sealed record PqcDecryptRequestDto(
    [Required] PqcEncryptedEnvelope Envelope);

public sealed record PqcDecryptResponseDto(
    string PlaintextBase64);

// ==========================================
// Multi-Tenancy Self-Service & Branding DTOs (Phase 22, Goal 1)
// ==========================================
#pragma warning disable CA1054, CA1056 // URI-like properties should not be strings in JSON DTO contracts
public sealed record TenantBrandingDto(
    string TenantId,
    string CompanyName,
    string? LogoUrl,
    string PrimaryColorHex,
    string AccentColorHex,
    string PortalTitle,
    string? CustomCss,
    DateTimeOffset UpdatedAt);

public sealed record UpdateTenantBrandingDto(
    [Required, MaxLength(256)] string CompanyName,
    [MaxLength(1024)] string? LogoUrl = null,
    [MaxLength(32)] string? PrimaryColorHex = null,
    [MaxLength(32)] string? AccentColorHex = null,
    [MaxLength(256)] string? PortalTitle = null,
    [MaxLength(8000)] string? CustomCss = null);
#pragma warning restore CA1054, CA1056

public sealed record TenantDepartmentQuotaDto(
    string Id,
    string TenantId,
    string DepartmentName,
    int AllocatedSeats,
    int ActiveSeats,
    bool EnforceStrictQuota,
    DateTimeOffset UpdatedAt);

public sealed record SetDepartmentQuotaDto(
    [Required, MaxLength(128)] string DepartmentName,
    [Range(0, 1000000)] int AllocatedSeats,
    bool EnforceStrictQuota = true);

public sealed record DepartmentUsageSummaryDto(
    string TenantId,
    int TotalAllocatedSeats,
    int TotalActiveSeats,
    IReadOnlyList<TenantDepartmentQuotaDto> Departments);


