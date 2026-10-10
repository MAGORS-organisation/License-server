using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Achilles.Data.Entities;
using Achilles.Data.Stores;
using Achilles.Domain.Entitlements;
using Xunit;

namespace Achilles.Data.Tests;

public sealed class EfFeatureEntitlementStoreTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AchillesDbContext> _options;

    public EfFeatureEntitlementStoreTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<AchillesDbContext>()
            .UseSqlite(_connection)
            .Options;
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private async Task<(string TenantId, string LicenseId)> SeedTenantAndLicenseAsync()
    {
        await using var db = new AchillesDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        string tenantId = $"ten_{Guid.NewGuid():N}";
        var tenant = new Tenant
        {
            Id = tenantId,
            Slug = $"tenant-{Guid.NewGuid():N}",
            Name = "Test Tenant",
            CreatedAt = DateTimeOffset.UtcNow
        };

        var product = new Product
        {
            Id = "cad-pro",
            TenantId = tenantId,
            Code = "cad-pro",
            Name = "CAD Pro",
            CreatedAt = DateTimeOffset.UtcNow
        };

        var policy = new Policy
        {
            Id = $"pol_{Guid.NewGuid():N}",
            TenantId = tenantId,
            ProductId = product.Id,
            Code = "floating-default",
            Name = "Floating Default",
            MaxSeats = 10
        };

        string licenseId = $"lic_{Guid.NewGuid():N}";
        var license = new LicenseEntity
        {
            Id = licenseId,
            TenantId = tenantId,
            PolicyId = policy.Id,
            CustomerRef = "Acme Corp",
            MaxSeats = 10,
            State = "active",
            KeyHash = "AABBCCDD",
            KeyLookup = [0xAA, 0xBB, 0xCC, 0xDD],
            IssuedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow,
            ExpiresAt = DateTimeOffset.UtcNow.AddYears(1)
        };

        db.Tenants.Add(tenant);
        db.Products.Add(product);
        db.Policies.Add(policy);
        db.Licenses.Add(license);

        await db.SaveChangesAsync();
        return (tenantId, licenseId);
    }

    [Fact]
    public async Task FeatureDefinitions_CreateListAndDelete_WorksCorrectly()
    {
        var (tenantId, _) = await SeedTenantAndLicenseAsync();
        await using var db = new AchillesDbContext(_options);
        var store = new EfFeatureEntitlementStore(db, TimeProvider.System);

        var model = new FeatureDefinitionModel(
            Id: Guid.NewGuid().ToString("N"),
            TenantId: tenantId,
            ProductId: "cad-pro",
            Code: "CAD_3D",
            Name: "3D CAD Modeling",
            Description: "Full 3D modeling kernel",
            MinVersion: "2025.0",
            MaxVersion: "2027.0",
            IsFloating: true,
            DefaultMaxSeats: 5,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

        var created = await store.CreateFeatureDefinitionAsync(model);
        created.Should().NotBeNull();
        created.Code.Should().Be("CAD_3D");

        var list = await store.ListFeatureDefinitionsAsync(tenantId, "cad-pro");
        list.Should().HaveCount(1);
        list[0].Code.Should().Be("CAD_3D");

        var fetched = await store.GetFeatureDefinitionByCodeAsync(tenantId, "CAD_3D");
        fetched.Should().NotBeNull();
        fetched!.Name.Should().Be("3D CAD Modeling");

        bool deleted = await store.DeleteFeatureDefinitionAsync(created.Id, tenantId);
        deleted.Should().BeTrue();

        var listAfter = await store.ListFeatureDefinitionsAsync(tenantId);
        listAfter.Should().BeEmpty();
    }

    [Fact]
    public async Task PackageSuites_CreateAndRetrieve_WorksCorrectly()
    {
        var (tenantId, _) = await SeedTenantAndLicenseAsync();
        await using var db = new AchillesDbContext(_options);
        var store = new EfFeatureEntitlementStore(db, TimeProvider.System);

        var suite = new PackageSuiteModel(
            Id: Guid.NewGuid().ToString("N"),
            TenantId: tenantId,
            ProductId: "cad-pro",
            Code: "SUITE_PREMIUM",
            Name: "Premium Suite",
            Description: "Includes 3D and FEA",
            FeatureCodes: ["CAD_3D", "FEA_SOLVER", "GPU_RENDER"],
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

        await store.CreatePackageSuiteAsync(suite);

        var fetched = await store.GetPackageSuiteByCodeAsync(tenantId, "SUITE_PREMIUM");
        fetched.Should().NotBeNull();
        fetched!.FeatureCodes.Should().Contain(["CAD_3D", "FEA_SOLVER", "GPU_RENDER"]);

        var suites = await store.ListPackageSuitesAsync(tenantId);
        suites.Should().HaveCount(1);

        bool deleted = await store.DeletePackageSuiteAsync(suite.Id, tenantId);
        deleted.Should().BeTrue();
    }

    [Fact]
    public async Task LicenseEntitlements_SetAndGet_WorksCorrectly()
    {
        var (tenantId, licenseId) = await SeedTenantAndLicenseAsync();
        await using var db = new AchillesDbContext(_options);
        var store = new EfFeatureEntitlementStore(db, TimeProvider.System);

        var entitlement = new LicenseEntitlementModel(
            Id: Guid.NewGuid().ToString("N"),
            TenantId: tenantId,
            LicenseId: licenseId,
            FeatureCode: "FEA_SOLVER",
            MaxSeats: 3,
            AllowedVersionRange: ">= 2026.0",
            IsEnabled: true,
            ParametersJson: "{\"threads\":8}",
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

        await store.SetLicenseEntitlementAsync(entitlement);

        var list = await store.GetEntitlementsForLicenseAsync(licenseId);
        list.Should().HaveCount(1);
        list[0].FeatureCode.Should().Be("FEA_SOLVER");
        list[0].MaxSeats.Should().Be(3);

        bool deleted = await store.DeleteLicenseEntitlementAsync(licenseId, "FEA_SOLVER");
        deleted.Should().BeTrue();

        var listAfter = await store.GetEntitlementsForLicenseAsync(licenseId);
        listAfter.Should().BeEmpty();
    }

    [Fact]
    public async Task TryAcquireFeatureLease_EnforcesCapacityAndCleansExpired()
    {
        var (tenantId, licenseId) = await SeedTenantAndLicenseAsync();
        await using var db = new AchillesDbContext(_options);
        var store = new EfFeatureEntitlementStore(db, TimeProvider.System);

        var now = DateTimeOffset.UtcNow;

        var req1 = new FeatureAcquisitionRequest(
            LeaseId: "lease_1",
            LicenseId: licenseId,
            TenantId: tenantId,
            FeatureCode: "SOLVER",
            Version: "1.0",
            Ttl: TimeSpan.FromMinutes(10));

        var req2 = new FeatureAcquisitionRequest(
            LeaseId: "lease_2",
            LicenseId: licenseId,
            TenantId: tenantId,
            FeatureCode: "SOLVER",
            Version: "1.0",
            Ttl: TimeSpan.FromMinutes(10));

        // Max seats = 1
        var l1 = await store.TryAcquireFeatureLeaseAsync(req1, maxSeats: 1, now);
        l1.Should().NotBeNull();

        // 2nd acquire fails because max seats = 1
        var l2 = await store.TryAcquireFeatureLeaseAsync(req2, maxSeats: 1, now);
        l2.Should().BeNull();

        // Acquire with future time where req1 has expired (15 minutes later)
        var futureNow = now.AddMinutes(15);
        var l2Future = await store.TryAcquireFeatureLeaseAsync(req2, maxSeats: 1, futureNow);
        l2Future.Should().NotBeNull();
        l2Future!.LeaseId.Should().Be("lease_2");

        // Release lease_2
        bool released = await store.ReleaseFeatureLeaseAsync("lease_2", "SOLVER");
        released.Should().BeTrue();

        int inUse = await store.GetActiveFeatureCountAsync(licenseId, "SOLVER", futureNow);
        inUse.Should().Be(0);
    }
}
