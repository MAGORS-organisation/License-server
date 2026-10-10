namespace Achilles.Domain.Entitlements;

public interface IFeatureEntitlementStore
{
    // Feature Definitions
    Task<IReadOnlyList<FeatureDefinitionModel>> ListFeatureDefinitionsAsync(string? tenantId, string? productId = null, CancellationToken ct = default);
    Task<FeatureDefinitionModel?> GetFeatureDefinitionByCodeAsync(string tenantId, string code, CancellationToken ct = default);
    Task<FeatureDefinitionModel> CreateFeatureDefinitionAsync(FeatureDefinitionModel model, CancellationToken ct = default);
    Task<bool> DeleteFeatureDefinitionAsync(string id, string? tenantId = null, CancellationToken ct = default);

    // Package Suites
    Task<IReadOnlyList<PackageSuiteModel>> ListPackageSuitesAsync(string? tenantId, string? productId = null, CancellationToken ct = default);
    Task<PackageSuiteModel?> GetPackageSuiteByCodeAsync(string tenantId, string code, CancellationToken ct = default);
    Task<PackageSuiteModel> CreatePackageSuiteAsync(PackageSuiteModel model, CancellationToken ct = default);
    Task<bool> DeletePackageSuiteAsync(string id, string? tenantId = null, CancellationToken ct = default);

    // License Entitlements
    Task<IReadOnlyList<LicenseEntitlementModel>> GetEntitlementsForLicenseAsync(string licenseId, CancellationToken ct = default);
    Task<LicenseEntitlementModel> SetLicenseEntitlementAsync(LicenseEntitlementModel model, CancellationToken ct = default);
    Task<bool> DeleteLicenseEntitlementAsync(string licenseId, string featureCode, CancellationToken ct = default);

    // Active Feature Leases
    Task<IReadOnlyList<ActiveFeatureLeaseModel>> GetActiveFeaturesForLeaseAsync(string leaseId, CancellationToken ct = default);
    Task<int> GetActiveFeatureCountAsync(string licenseId, string featureCode, DateTimeOffset now, CancellationToken ct = default);
    Task<ActiveFeatureLeaseModel?> TryAcquireFeatureLeaseAsync(FeatureAcquisitionRequest request, int? maxSeats, DateTimeOffset now, CancellationToken ct = default);
    Task<bool> ReleaseFeatureLeaseAsync(string leaseId, string featureCode, CancellationToken ct = default);
    Task<int> ReleaseAllFeaturesForLeaseAsync(string leaseId, CancellationToken ct = default);
    Task<IReadOnlyList<FeatureUsageMetric>> GetFeatureUsageMetricsAsync(string? tenantId, DateTimeOffset now, CancellationToken ct = default);
}
