using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using System.Xml;

namespace Symbolon.Format;

/// <summary>
/// Top-level claims for a Symbolon License File according to spec/03-symlic-1.md.
/// </summary>
public sealed record LicenseClaims
{
    [JsonPropertyName("iss")]
    public required string Iss { get; init; }

    [JsonPropertyName("sub")]
    public required string Sub { get; init; }

    [JsonPropertyName("aud")]
    public required string Aud { get; init; }

    [JsonPropertyName("jti")]
    public required string Jti { get; init; }

    [JsonPropertyName("iat")]
    public required long Iat { get; init; }

    [JsonPropertyName("nbf")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Nbf { get; init; }

    [JsonPropertyName("exp")]
    public required long Exp { get; init; }

    [JsonPropertyName("symlic")]
    public required SymlicClaims Symlic { get; init; }
}

public sealed record SymlicClaims
{
    [JsonPropertyName("v")]
    public required int V { get; init; }

    [JsonPropertyName("profile")]
    public required string Profile { get; init; }

    [JsonPropertyName("requiredAlgs")]
    public required IReadOnlyList<string> RequiredAlgs { get; init; }

    [JsonPropertyName("license")]
    public required LicenseMetadata License { get; init; }

    [JsonPropertyName("limits")]
    public required LicenseLimits Limits { get; init; }

    [JsonPropertyName("entitlements")]
    public IReadOnlyList<EntitlementClaim> Entitlements { get; init; } = [];

    [JsonPropertyName("binding")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public BindingClaim? Binding { get; init; }

    [JsonPropertyName("policy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PolicyClaim? Policy { get; init; }
}

public sealed record LicenseMetadata
{
    [JsonPropertyName("key")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Key { get; init; }

    [JsonPropertyName("model")]
    public required string Model { get; init; }

    [JsonPropertyName("state")]
    public required string State { get; init; }

    [JsonPropertyName("issuedAt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? IssuedAt { get; init; }

    [JsonPropertyName("expiresAt")]
    public string? ExpiresAt { get; init; }

    [JsonPropertyName("maintenanceUntil")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MaintenanceUntil { get; init; }

    [JsonPropertyName("customer")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public CustomerMetadata? Customer { get; init; }
}

public sealed record CustomerMetadata
{
    [JsonPropertyName("ref")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Ref { get; init; }

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Name { get; init; }
}

public sealed record LicenseLimits
{
    [JsonPropertyName("maxSeats")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxSeats { get; init; }

    [JsonPropertyName("seatUnit")]
    public required string SeatUnit { get; init; }

    [JsonPropertyName("overageStrategy")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OverageStrategy { get; init; }

    [JsonPropertyName("maxRelays")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MaxRelays { get; init; }
}

public sealed record EntitlementClaim
{
    [JsonPropertyName("code")]
    public required string Code { get; init; }

    [JsonPropertyName("value")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public decimal? Value { get; init; }

    [JsonPropertyName("period")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Period { get; init; }
}

public sealed record BindingClaim
{
    [JsonPropertyName("fingerprint")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Fingerprint { get; init; }

    [JsonPropertyName("matching")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Matching { get; init; }

    [JsonPropertyName("components")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyDictionary<string, string>? Components { get; init; }
}

public sealed record PolicyClaim
{
    [JsonPropertyName("lease")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public LeasePolicyClaim? Lease { get; init; }

    [JsonPropertyName("clockSkewTolerance")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClockSkewTolerance { get; init; }

    [JsonPropertyName("revocation")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public RevocationPolicyClaim? Revocation { get; init; }

    public TimeSpan GetClockSkewToleranceOrDefault()
    {
        if (string.IsNullOrWhiteSpace(ClockSkewTolerance))
        {
            return TimeSpan.FromMinutes(5);
        }

        try
        {
            return XmlConvert.ToTimeSpan(ClockSkewTolerance);
        }
        catch (FormatException)
        {
            return TimeSpan.FromMinutes(5);
        }
    }
}

public sealed record LeasePolicyClaim
{
    [JsonPropertyName("ttl")]
    public required string Ttl { get; init; }

    [JsonPropertyName("graceTtl")]
    public required string GraceTtl { get; init; }
}

public sealed record RevocationPolicyClaim
{
    [JsonPropertyName("url")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "JSON DTO mapping")]
    public string? Url { get; init; }

    [JsonPropertyName("maxAge")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MaxAge { get; init; }
}
