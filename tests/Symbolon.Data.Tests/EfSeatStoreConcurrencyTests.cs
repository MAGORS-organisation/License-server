using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Symbolon.Data.Entities;
using Symbolon.Data.Stores;
using Symbolon.Domain;
using Xunit;

namespace Symbolon.Data.Tests;

public sealed class EfSeatStoreConcurrencyTests : IDisposable
{
    private readonly string _connectionString = $"Data Source=concurrency_{Guid.NewGuid():N};Mode=Memory;Cache=Shared";
    private readonly SqliteConnection _keepAliveConnection;
    private readonly DbContextOptions<SymbolonDbContext> _options;

    public EfSeatStoreConcurrencyTests()
    {
        _keepAliveConnection = new SqliteConnection(_connectionString);
        _keepAliveConnection.Open();
        _options = new DbContextOptionsBuilder<SymbolonDbContext>()
            .UseSqlite(_connectionString)
            .Options;
    }

    public void Dispose()
    {
        _keepAliveConnection.Dispose();
    }

    private async Task SeedLicenseAsync(string licenseId, int seats)
    {
        await using var db = new SymbolonDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        var tenant = new Tenant { Id = "ten_c1", Slug = "acme_c", Name = "Acme Concurrency", CreatedAt = DateTimeOffset.UtcNow };
        var product = new Product { Id = "prd_c1", TenantId = tenant.Id, Code = "prd_c", Name = "Product Concurrency", CreatedAt = DateTimeOffset.UtcNow };
        var policy = new Policy { Id = "pol_c1", TenantId = tenant.Id, ProductId = product.Id, Code = "pol_c", Name = "Policy Concurrency", MaxSeats = seats };
        var license = new LicenseEntity
        {
            Id = licenseId,
            TenantId = tenant.Id,
            PolicyId = policy.Id,
            KeyLookup = [0x09, 0x09],
            KeyHash = "hash_c",
            State = "active",
            MaxSeats = seats,
            IssuedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };

        db.Tenants.Add(tenant);
        db.Products.Add(product);
        db.Policies.Add(policy);
        db.Licenses.Add(license);

        for (int i = 1; i <= seats; i++)
        {
            db.Seats.Add(new SeatEntity
            {
                LicenseId = licenseId,
                SeatNo = i
            });
        }

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task TryAcquireOne_UnderHighConcurrency_GuaranteesZeroDoubleAllocation()
    {
        const string licenseId = "lic_concurrency_race";
        const int totalSeats = 1;
        const int concurrentClients = 10;

        await SeedLicenseAsync(licenseId, totalSeats);

        var now = DateTimeOffset.UtcNow;
        var ttl = TimeSpan.FromMinutes(15);

        // Barrier to synchronize all concurrent tasks so they hit TryAcquireOne at the exact same millisecond
        using var barrier = new SemaphoreSlim(0, concurrentClients);
        var tasks = new Task<SeatAllocation?>[concurrentClients];

        for (int i = 0; i < concurrentClients; i++)
        {
            int clientId = i;
            tasks[i] = Task.Run(async () =>
            {
                // Each request gets its own DbContext instance as in ASP.NET Core request pipeline
                await using var db = new SymbolonDbContext(_options);
                var store = new EfSeatStore(db);

                barrier.Release();
                await Task.Yield();

                return await store.TryAcquireOneAsync(
                    licenseId,
                    $"sha256:fp_{clientId}",
                    $"mch_{clientId}",
                    now,
                    ttl);
            });
        }

        var results = await Task.WhenAll(tasks);

        var successfulAllocations = results.Where(r => r is not null).ToList();
        var deniedAllocations = results.Where(r => r is null).ToList();

        // Exactly 1 client must succeed, all other 9 must be safely rejected
        Assert.Single(successfulAllocations);
        Assert.Equal(concurrentClients - 1, deniedAllocations.Count);

        // Verify DB integrity
        await using var verifyDb = new SymbolonDbContext(_options);
        var seat = await verifyDb.Seats.SingleAsync(s => s.LicenseId == licenseId && s.SeatNo == 1);
        Assert.NotNull(seat.LeaseId);
        Assert.Equal(successfulAllocations[0]!.LeaseId, seat.LeaseId);
    }
}
