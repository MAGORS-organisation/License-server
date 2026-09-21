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

public sealed record QueuedResponseDto
{
    [JsonPropertyName("ticket")]
    public required string Ticket { get; init; }

    [JsonPropertyName("position")]
    public required int Position { get; init; }

    [JsonPropertyName("estimatedWait")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EstimatedWait { get; init; }
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
    public const string Unauthorized = "https://symbolon.dev/errors/unauthorized";
    public const string Forbidden = "https://symbolon.dev/errors/forbidden";
}

