using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

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
    public bool OfflineAllowed { get; init; } = true;
    public string CryptoProfile { get; init; } = "hybrid-v1";
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
    int LeaseTtlSeconds);

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

public sealed record ActivationRequestDto
{
    [Required] public required string LicenseKey { get; init; }
    [Required] public required IReadOnlyDictionary<string, string> FingerprintComponents { get; init; }
    public string? MachineId { get; init; }
}

public sealed record ActivationResponseDto(
    string ActivationId,
    string LicenseId,
    string Fingerprint,
    string State,
    DateTimeOffset ActivatedAt);

public sealed record BorrowRequestDto(
    [Range(1, 30)] int Days);

public sealed record BorrowResponseDto(
    string LeaseId,
    DateTimeOffset BorrowedUntil,
    string Token);

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
