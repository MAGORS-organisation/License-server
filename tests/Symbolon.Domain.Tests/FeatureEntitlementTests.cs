using FluentAssertions;
using Symbolon.Domain.Entitlements;
using Xunit;

namespace Symbolon.Domain.Tests;

public sealed class FeatureEntitlementTests
{
    private sealed class InMemoryFeatureEntitlementStore : IFeatureEntitlementStore
    {
        private readonly List<FeatureDefinitionModel> _definitions = new();
        private readonly List<PackageSuiteModel> _suites = new();
        private readonly List<LicenseEntitlementModel> _entitlements = new();
        private readonly List<ActiveFeatureLeaseModel> _activeLeases = new();
        private readonly Dictionary<string, int> _denialCounters = new();

        public Task<FeatureDefinitionModel> CreateFeatureDefinitionAsync(FeatureDefinitionModel model, CancellationToken ct = default)
        {
            _definitions.Add(model);
            return Task.FromResult(model);
        }

        public Task<FeatureDefinitionModel?> GetFeatureDefinitionByCodeAsync(string tenantId, string code, CancellationToken ct = default)
        {
            var def = _definitions.FirstOrDefault(d =>
                string.Equals(d.TenantId, tenantId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(d.Code, code, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(def);
        }

        public Task<IReadOnlyList<FeatureDefinitionModel>> ListFeatureDefinitionsAsync(string? tenantId, string? productId, CancellationToken ct = default)
        {
            var list = _definitions.Where(d =>
                (tenantId == null || string.Equals(d.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)) &&
                (productId == null || string.Equals(d.ProductId, productId, StringComparison.OrdinalIgnoreCase))).ToList();
            return Task.FromResult<IReadOnlyList<FeatureDefinitionModel>>(list);
        }

        public Task<bool> DeleteFeatureDefinitionAsync(string id, string? tenantId = null, CancellationToken ct = default)
        {
            int removed = _definitions.RemoveAll(d => d.Id == id && (tenantId == null || d.TenantId == tenantId));
            return Task.FromResult(removed > 0);
        }

        public Task<PackageSuiteModel> CreatePackageSuiteAsync(PackageSuiteModel model, CancellationToken ct = default)
        {
            _suites.Add(model);
            return Task.FromResult(model);
        }

        public Task<PackageSuiteModel?> GetPackageSuiteByCodeAsync(string tenantId, string code, CancellationToken ct = default)
        {
            var s = _suites.FirstOrDefault(x =>
                string.Equals(x.TenantId, tenantId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Code, code, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(s);
        }

        public Task<IReadOnlyList<PackageSuiteModel>> ListPackageSuitesAsync(string? tenantId, string? productId, CancellationToken ct = default)
        {
            var list = _suites.Where(s =>
                (tenantId == null || string.Equals(s.TenantId, tenantId, StringComparison.OrdinalIgnoreCase)) &&
                (productId == null || string.Equals(s.ProductId, productId, StringComparison.OrdinalIgnoreCase))).ToList();
            return Task.FromResult<IReadOnlyList<PackageSuiteModel>>(list);
        }

        public Task<bool> DeletePackageSuiteAsync(string id, string? tenantId = null, CancellationToken ct = default)
        {
            int removed = _suites.RemoveAll(s => s.Id == id && (tenantId == null || s.TenantId == tenantId));
            return Task.FromResult(removed > 0);
        }

        public Task<LicenseEntitlementModel> SetLicenseEntitlementAsync(LicenseEntitlementModel model, CancellationToken ct = default)
        {
            _entitlements.RemoveAll(e => e.LicenseId == model.LicenseId && string.Equals(e.FeatureCode, model.FeatureCode, StringComparison.OrdinalIgnoreCase));
            _entitlements.Add(model);
            return Task.FromResult(model);
        }

        public Task<IReadOnlyList<LicenseEntitlementModel>> GetEntitlementsForLicenseAsync(string licenseId, CancellationToken ct = default)
        {
            var list = _entitlements.Where(e => e.LicenseId == licenseId).ToList();
            return Task.FromResult<IReadOnlyList<LicenseEntitlementModel>>(list);
        }

        public Task<bool> DeleteLicenseEntitlementAsync(string licenseId, string featureCode, CancellationToken ct = default)
        {
            int removed = _entitlements.RemoveAll(e => e.LicenseId == licenseId && string.Equals(e.FeatureCode, featureCode, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(removed > 0);
        }

        public Task<ActiveFeatureLeaseModel?> TryAcquireFeatureLeaseAsync(FeatureAcquisitionRequest request, int? maxSeats, DateTimeOffset now, CancellationToken ct = default)
        {
            // Remove expired leases
            _activeLeases.RemoveAll(l => l.ExpiresAt <= now);

            // If already holding this feature for this lease, refresh and return existing
            var existing = _activeLeases.FirstOrDefault(l =>
                l.LeaseId == request.LeaseId &&
                string.Equals(l.FeatureCode, request.FeatureCode, StringComparison.OrdinalIgnoreCase));

            if (existing is not null)
            {
                var refreshed = existing with { ExpiresAt = now.Add(request.Ttl) };
                _activeLeases.Remove(existing);
                _activeLeases.Add(refreshed);
                return Task.FromResult<ActiveFeatureLeaseModel?>(refreshed);
            }

            int currentInUse = _activeLeases.Count(l =>
                l.LicenseId == request.LicenseId &&
                string.Equals(l.FeatureCode, request.FeatureCode, StringComparison.OrdinalIgnoreCase));

            if (maxSeats.HasValue && currentInUse >= maxSeats.Value)
            {
                string key = $"{request.LicenseId}:{request.FeatureCode}";
                _denialCounters[key] = _denialCounters.GetValueOrDefault(key) + 1;
                return Task.FromResult<ActiveFeatureLeaseModel?>(null);
            }

            var lease = new ActiveFeatureLeaseModel(
                Id: Guid.NewGuid().ToString("N"),
                TenantId: request.TenantId,
                LicenseId: request.LicenseId,
                LeaseId: request.LeaseId,
                FeatureCode: request.FeatureCode,
                AcquiredVersion: request.Version,
                AcquiredAt: now,
                ExpiresAt: now.Add(request.Ttl));

            _activeLeases.Add(lease);
            return Task.FromResult<ActiveFeatureLeaseModel?>(lease);
        }

        public Task<bool> ReleaseFeatureLeaseAsync(string leaseId, string featureCode, CancellationToken ct = default)
        {
            int removed = _activeLeases.RemoveAll(l =>
                l.LeaseId == leaseId &&
                string.Equals(l.FeatureCode, featureCode, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(removed > 0);
        }

        public Task<int> ReleaseAllFeaturesForLeaseAsync(string leaseId, CancellationToken ct = default)
        {
            int removed = _activeLeases.RemoveAll(l => l.LeaseId == leaseId);
            return Task.FromResult(removed);
        }

        public Task<int> GetActiveFeatureCountAsync(string licenseId, string featureCode, DateTimeOffset now, CancellationToken ct = default)
        {
            int count = _activeLeases.Count(l =>
                l.LicenseId == licenseId &&
                string.Equals(l.FeatureCode, featureCode, StringComparison.OrdinalIgnoreCase) &&
                l.ExpiresAt > now);
            return Task.FromResult(count);
        }

        public Task<IReadOnlyList<ActiveFeatureLeaseModel>> GetActiveFeaturesForLeaseAsync(string leaseId, CancellationToken ct = default)
        {
            var list = _activeLeases.Where(l => l.LeaseId == leaseId).ToList();
            return Task.FromResult<IReadOnlyList<ActiveFeatureLeaseModel>>(list);
        }

        public Task<IReadOnlyList<FeatureUsageMetric>> GetFeatureUsageMetricsAsync(string? tenantId, DateTimeOffset now, CancellationToken ct = default)
        {
            var list = _definitions
                .Where(d => tenantId == null || string.Equals(d.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
                .Select(d =>
                {
                    int inUse = _activeLeases.Count(l =>
                        string.Equals(l.FeatureCode, d.Code, StringComparison.OrdinalIgnoreCase) &&
                        l.ExpiresAt > now);

                    string key = $"{d.TenantId}:{d.Code}";
                    int denials = _denialCounters.GetValueOrDefault(key);
                    var activeLeases = _activeLeases
                        .Where(l => string.Equals(l.FeatureCode, d.Code, StringComparison.OrdinalIgnoreCase))
                        .Select(l => l.LeaseId)
                        .Distinct()
                        .ToList();

                    return new FeatureUsageMetric(d.Code, d.Name, d.ProductId, d.DefaultMaxSeats, inUse, denials, activeLeases);
                }).ToList();

            return Task.FromResult<IReadOnlyList<FeatureUsageMetric>>(list);
        }
    }

    [Fact]
    public void VersionRangeMatcher_WildcardAndExact_EvaluatesCorrectly()
    {
        VersionRangeMatcher.IsVersionAllowed("2026.1", "*").Should().BeTrue();
        VersionRangeMatcher.IsVersionAllowed("2026.1", null).Should().BeTrue();
        VersionRangeMatcher.IsVersionAllowed("2026.1", "2026.1").Should().BeTrue();
        VersionRangeMatcher.IsVersionAllowed("2025.2", "2026.1").Should().BeFalse();
        VersionRangeMatcher.IsVersionAllowed("2026.3", "latest").Should().BeTrue();
    }

    [Fact]
    public void VersionRangeMatcher_WildcardPrefix_MatchesExpected()
    {
        VersionRangeMatcher.IsVersionAllowed("2026.1.4", "2026.*").Should().BeTrue();
        VersionRangeMatcher.IsVersionAllowed("2026.2", "2026.*").Should().BeTrue();
        VersionRangeMatcher.IsVersionAllowed("2025.9", "2026.*").Should().BeFalse();
    }

    [Fact]
    public void VersionRangeMatcher_Interval_MatchesWithinAndRejectsOutside()
    {
        string range = ">= 2025.0 and <= 2027.0";
        VersionRangeMatcher.IsVersionAllowed("2026.0", range).Should().BeTrue();
        VersionRangeMatcher.IsVersionAllowed("2025.0", range).Should().BeTrue();
        VersionRangeMatcher.IsVersionAllowed("2027.0", range).Should().BeTrue();
        VersionRangeMatcher.IsVersionAllowed("2024.9", range).Should().BeFalse();
        VersionRangeMatcher.IsVersionAllowed("2028.0", range).Should().BeFalse();
    }

    [Fact]
    public async Task ExpandFeaturesAsync_ExpandsPackageSuites()
    {
        var store = new InMemoryFeatureEntitlementStore();
        var engine = new FeatureEntitlementEngine(store, TimeProvider.System);

        await store.CreatePackageSuiteAsync(new PackageSuiteModel(
            Id: "s1",
            TenantId: "t1",
            ProductId: "cad",
            Code: "SUITE_ALL",
            Name: "All CAD modules",
            Description: null,
            FeatureCodes: ["CAD_3D", "FEA_SOLVER", "GPU_RENDER"],
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow));

        var expanded = await engine.ExpandFeaturesAsync("t1", ["SUITE_ALL", "EXTRA_PLUGIN"]);

        expanded.Should().Contain(["CAD_3D", "FEA_SOLVER", "GPU_RENDER", "EXTRA_PLUGIN"]);
        expanded.Should().HaveCount(4);
    }

    [Fact]
    public async Task AcquireFeatureAsync_WhenCapacityExceeded_Denies()
    {
        var store = new InMemoryFeatureEntitlementStore();
        var engine = new FeatureEntitlementEngine(store, TimeProvider.System);

        // Feature with limit of 2 seats
        await store.CreateFeatureDefinitionAsync(new FeatureDefinitionModel(
            Id: "f1",
            TenantId: "t1",
            ProductId: "cad",
            Code: "FEA_SOLVER",
            Name: "FEA Solver",
            Description: null,
            MinVersion: null,
            MaxVersion: null,
            IsFloating: true,
            DefaultMaxSeats: 2,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow));

        var req1 = new FeatureAcquisitionRequest("lease-1", "lic-1", "t1", "FEA_SOLVER", "2026.1", TimeSpan.FromMinutes(5));
        var req2 = new FeatureAcquisitionRequest("lease-2", "lic-1", "t1", "FEA_SOLVER", "2026.1", TimeSpan.FromMinutes(5));
        var req3 = new FeatureAcquisitionRequest("lease-3", "lic-1", "t1", "FEA_SOLVER", "2026.1", TimeSpan.FromMinutes(5));

        var res1 = await engine.AcquireFeatureAsync(req1);
        var res2 = await engine.AcquireFeatureAsync(req2);
        var res3 = await engine.AcquireFeatureAsync(req3);

        res1.Success.Should().BeTrue();
        res1.InUse.Should().Be(1);

        res2.Success.Should().BeTrue();
        res2.InUse.Should().Be(2);

        res3.Success.Should().BeFalse();
        res3.Reason.Should().Be("feature-capacity-exceeded");
    }

    [Fact]
    public async Task AcquireFeatureAsync_WhenVersionNotAllowed_Denies()
    {
        var store = new InMemoryFeatureEntitlementStore();
        var engine = new FeatureEntitlementEngine(store, TimeProvider.System);

        // License entitlement restricted to version 2026.*
        await store.SetLicenseEntitlementAsync(new LicenseEntitlementModel(
            Id: "e1",
            TenantId: "t1",
            LicenseId: "lic-1",
            FeatureCode: "FEA_SOLVER",
            MaxSeats: 5,
            AllowedVersionRange: "2026.*",
            IsEnabled: true,
            ParametersJson: null,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow));

        var reqValid = new FeatureAcquisitionRequest("lease-1", "lic-1", "t1", "FEA_SOLVER", "2026.2", TimeSpan.FromMinutes(5));
        var reqInvalid = new FeatureAcquisitionRequest("lease-2", "lic-1", "t1", "FEA_SOLVER", "2025.9", TimeSpan.FromMinutes(5));

        var resValid = await engine.AcquireFeatureAsync(reqValid);
        var resInvalid = await engine.AcquireFeatureAsync(reqInvalid);

        resValid.Success.Should().BeTrue();
        resInvalid.Success.Should().BeFalse();
        resInvalid.Reason.Should().Be("version-not-allowed");
    }

    [Fact]
    public async Task AcquireFeatureAsync_WhenDisabled_Denies()
    {
        var store = new InMemoryFeatureEntitlementStore();
        var engine = new FeatureEntitlementEngine(store, TimeProvider.System);

        await store.SetLicenseEntitlementAsync(new LicenseEntitlementModel(
            Id: "e1",
            TenantId: "t1",
            LicenseId: "lic-1",
            FeatureCode: "FEA_SOLVER",
            MaxSeats: 5,
            AllowedVersionRange: "*",
            IsEnabled: false,
            ParametersJson: null,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow));

        var req = new FeatureAcquisitionRequest("lease-1", "lic-1", "t1", "FEA_SOLVER", "2026.1", TimeSpan.FromMinutes(5));
        var res = await engine.AcquireFeatureAsync(req);

        res.Success.Should().BeFalse();
        res.Reason.Should().Be("feature-disabled");
    }

    [Fact]
    public async Task AcquireFeatureAsync_WhenNonFloating_GrantsWithoutSeatTracking()
    {
        var store = new InMemoryFeatureEntitlementStore();
        var engine = new FeatureEntitlementEngine(store, TimeProvider.System);

        await store.CreateFeatureDefinitionAsync(new FeatureDefinitionModel(
            Id: "f1",
            TenantId: "t1",
            ProductId: "cad",
            Code: "VIEWER_ONLY",
            Name: "Viewer",
            Description: null,
            MinVersion: null,
            MaxVersion: null,
            IsFloating: false,
            DefaultMaxSeats: null,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow));

        var req = new FeatureAcquisitionRequest("lease-1", "lic-1", "t1", "VIEWER_ONLY", "2026.1", TimeSpan.FromMinutes(5));
        var res = await engine.AcquireFeatureAsync(req);

        res.Success.Should().BeTrue();
        res.MaxSeats.Should().BeNull();
    }

    [Fact]
    public async Task ReleaseFeatureAsync_AndReleaseAll_FreesSeats()
    {
        var store = new InMemoryFeatureEntitlementStore();
        var engine = new FeatureEntitlementEngine(store, TimeProvider.System);

        await store.CreateFeatureDefinitionAsync(new FeatureDefinitionModel(
            Id: "f1",
            TenantId: "t1",
            ProductId: "cad",
            Code: "FEA_SOLVER",
            Name: "FEA Solver",
            Description: null,
            MinVersion: null,
            MaxVersion: null,
            IsFloating: true,
            DefaultMaxSeats: 1,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow));

        var req1 = new FeatureAcquisitionRequest("lease-1", "lic-1", "t1", "FEA_SOLVER", "2026.1", TimeSpan.FromMinutes(5));
        var req2 = new FeatureAcquisitionRequest("lease-2", "lic-1", "t1", "FEA_SOLVER", "2026.1", TimeSpan.FromMinutes(5));

        var res1 = await engine.AcquireFeatureAsync(req1);
        res1.Success.Should().BeTrue();

        // 2nd acquisition denied because limit = 1
        var res2Denied = await engine.AcquireFeatureAsync(req2);
        res2Denied.Success.Should().BeFalse();

        // Release lease-1 feature
        var rel = await engine.ReleaseFeatureAsync("lease-1", "FEA_SOLVER");
        rel.Success.Should().BeTrue();

        // Now 2nd acquisition succeeds
        var res2Allowed = await engine.AcquireFeatureAsync(req2);
        res2Allowed.Success.Should().BeTrue();

        // ReleaseAll for lease-2
        int releasedCount = await engine.ReleaseAllFeaturesForLeaseAsync("lease-2");
        releasedCount.Should().Be(1);
    }
}
