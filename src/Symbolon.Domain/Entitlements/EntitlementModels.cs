namespace Symbolon.Domain.Entitlements;

public sealed record FeatureDefinitionModel(
    string Id,
    string TenantId,
    string? ProductId,
    string Code,
    string Name,
    string? Description,
    string? MinVersion,
    string? MaxVersion,
    bool IsFloating,
    int? DefaultMaxSeats,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record PackageSuiteModel(
    string Id,
    string TenantId,
    string? ProductId,
    string Code,
    string Name,
    string? Description,
    IReadOnlyList<string> FeatureCodes,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record LicenseEntitlementModel(
    string Id,
    string TenantId,
    string LicenseId,
    string FeatureCode,
    int? MaxSeats,
    string? AllowedVersionRange,
    bool IsEnabled,
    string? ParametersJson,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record ActiveFeatureLeaseModel(
    string Id,
    string TenantId,
    string LicenseId,
    string LeaseId,
    string FeatureCode,
    string? AcquiredVersion,
    DateTimeOffset AcquiredAt,
    DateTimeOffset ExpiresAt);

public sealed record FeatureAcquisitionRequest(
    string LeaseId,
    string LicenseId,
    string TenantId,
    string FeatureCode,
    string? Version,
    TimeSpan Ttl);

public sealed record FeatureAcquisitionResult(
    bool Success,
    string FeatureCode,
    string? Version,
    string? Reason = null,
    int? InUse = null,
    int? MaxSeats = null)
{
    public static FeatureAcquisitionResult Ok(string featureCode, string? version, int inUse, int? maxSeats) =>
        new(true, featureCode, version, null, inUse, maxSeats);

    public static FeatureAcquisitionResult Denied(string featureCode, string? version, string reason, int? inUse = null, int? maxSeats = null) =>
        new(false, featureCode, version, reason, inUse, maxSeats);
}

public sealed record FeatureReleaseResult(
    bool Success,
    string FeatureCode,
    string? Reason = null)
{
    public static FeatureReleaseResult Ok(string featureCode) => new(true, featureCode, null);
    public static FeatureReleaseResult NotFound(string featureCode) => new(false, featureCode, "feature-not-held");
}

public sealed record FeatureUsageMetric(
    string FeatureCode,
    string Name,
    string? ProductCode,
    int? MaxSeats,
    int InUseSeats,
    int DenialsCount,
    IReadOnlyList<string> ActiveLeaseIds);
