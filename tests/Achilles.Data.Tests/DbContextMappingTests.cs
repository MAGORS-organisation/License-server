using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Achilles.Data.Entities;
using Xunit;

namespace Achilles.Data.Tests;

public sealed class DbContextMappingTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<AchillesDbContext> _options;

    public DbContextMappingTests()
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

    [Fact]
    public async Task Can_Create_Schema_And_Persist_Tenant_Product_Policy_License_And_Seats()
    {
        await using var db = new AchillesDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        var tenant = new Tenant
        {
            Id = "ten_test123",
            Slug = "acme-corp",
            Name = "Acme Corporation",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Tenants.Add(tenant);

        var product = new Product
        {
            Id = "prd_cad",
            TenantId = tenant.Id,
            Code = "acme-cad",
            Name = "Acme CAD Suite",
            PlatformsJson = "[\"windows\",\"linux\"]",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Products.Add(product);

        var entitlement = new Entitlement
        {
            Id = "ent_export",
            ProductId = product.Id,
            Code = "module.cad-export",
            Name = "CAD Export Module",
            Type = "boolean"
        };
        db.Entitlements.Add(entitlement);

        var policy = new Policy
        {
            Id = "pol_floating",
            TenantId = tenant.Id,
            ProductId = product.Id,
            Code = "floating-annual",
            Name = "Floating Annual License",
            LicenseModel = "floating",
            MaxSeats = 3,
            SeatUnit = "machine",
            LeaseTtlSeconds = 600
        };
        db.Policies.Add(policy);

        var license = new LicenseEntity
        {
            Id = "lic_01TEST",
            TenantId = tenant.Id,
            PolicyId = policy.Id,
            KeyLookup = [1, 2, 3, 4],
            KeyHash = "hash_123",
            CustomerRef = "CUST-42",
            State = "active",
            MaxSeats = 3,
            IssuedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Licenses.Add(license);

        for (int i = 1; i <= 3; i++)
        {
            db.Seats.Add(new SeatEntity
            {
                LicenseId = license.Id,
                SeatNo = i
            });
        }

        await db.SaveChangesAsync();

        // Verify read back
        await using var readDb = new AchillesDbContext(_options);
        var loadedTenant = await readDb.Tenants
            .Include(t => t.Products)
            .ThenInclude(p => p.Policies)
            .FirstOrDefaultAsync(t => t.Id == tenant.Id);

        Assert.NotNull(loadedTenant);
        Assert.Equal("acme-corp", loadedTenant.Slug);
        Assert.Single(loadedTenant.Products);
        Assert.Single(loadedTenant.Products.First().Policies);

        var seatCount = await readDb.Seats.CountAsync(s => s.LicenseId == license.Id);
        Assert.Equal(3, seatCount);
    }
}
