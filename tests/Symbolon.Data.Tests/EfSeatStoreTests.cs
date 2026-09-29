using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Symbolon.Data.Entities;
using Symbolon.Data.Stores;
using Symbolon.Domain;
using Xunit;

namespace Symbolon.Data.Tests;

public sealed class EfSeatStoreTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<SymbolonDbContext> _options;

    public EfSeatStoreTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<SymbolonDbContext>()
            .UseSqlite(_connection)
            .Options;
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private async Task SeedLicenseAsync(string licenseId, int seats)
    {
        await using var db = new SymbolonDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        var tenant = new Tenant { Id = "ten_1", Slug = "acme", Name = "Acme", CreatedAt = DateTimeOffset.UtcNow };
        var product = new Product { Id = "prd_1", TenantId = tenant.Id, Code = "prd", Name = "Product", CreatedAt = DateTimeOffset.UtcNow };
        var policy = new Policy { Id = "pol_1", TenantId = tenant.Id, ProductId = product.Id, Code = "pol", Name = "Policy", MaxSeats = seats };
        var license = new LicenseEntity
        {
            Id = licenseId,
            TenantId = tenant.Id,
            PolicyId = policy.Id,
            KeyLookup = [0x01, 0x02],
            KeyHash = "hash",
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
    public async Task TryAcquireOne_Allocates_Seat_Until_Exhausted()
    {
        const string licenseId = "lic_test_acquire";
        await SeedLicenseAsync(licenseId, 2);

        await using var db = new SymbolonDbContext(_options);
        var store = new EfSeatStore(db);
        var now = DateTimeOffset.UtcNow;
        var ttl = TimeSpan.FromMinutes(10);

        var s1 = await store.TryAcquireOneAsync(licenseId, "sha256:fp1", "mch1", now, ttl);
        Assert.NotNull(s1);
        Assert.Equal(1, s1.SeatNo);

        var s2 = await store.TryAcquireOneAsync(licenseId, "sha256:fp2", "mch2", now, ttl);
        Assert.NotNull(s2);
        Assert.Equal(2, s2.SeatNo);

        // 3rd attempt should fail because max_seats is 2
        var s3 = await store.TryAcquireOneAsync(licenseId, "sha256:fp3", "mch3", now, ttl);
        Assert.Null(s3);
    }

    [Fact]
    public async Task TryRenew_Succeeds_And_Detects_Seq_Replay()
    {
        const string licenseId = "lic_test_renew";
        await SeedLicenseAsync(licenseId, 1);

        await using var db = new SymbolonDbContext(_options);
        var store = new EfSeatStore(db);
        var now = DateTimeOffset.UtcNow;
        var ttl = TimeSpan.FromMinutes(10);
        var resWindow = TimeSpan.FromMinutes(5);

        var alloc = await store.TryAcquireOneAsync(licenseId, "sha256:fp1", "mch1", now, ttl);
        Assert.NotNull(alloc);

        // Valid renew
        var outcome = await store.TryRenewAsync(alloc.LeaseId!, "sha256:fp1", 0, now.AddMinutes(2), ttl, resWindow);
        Assert.Equal(RenewOutcomeType.Renewed, outcome.Type);
        Assert.NotNull(outcome.Allocation);
        Assert.Equal(1, outcome.Allocation.LeaseSeq);

        // Replay with stale seq 0 should fail with SeqReplay
        var replay = await store.TryRenewAsync(alloc.LeaseId!, "sha256:fp1", 0, now.AddMinutes(4), ttl, resWindow);
        Assert.Equal(RenewOutcomeType.SeqReplay, replay.Type);
    }

    [Fact]
    public async Task TryRelease_Frees_Seat_For_New_Acquisition()
    {
        const string licenseId = "lic_test_release";
        await SeedLicenseAsync(licenseId, 1);

        await using var db = new SymbolonDbContext(_options);
        var store = new EfSeatStore(db);
        var now = DateTimeOffset.UtcNow;
        var ttl = TimeSpan.FromMinutes(10);

        var alloc = await store.TryAcquireOneAsync(licenseId, "sha256:fp1", "mch1", now, ttl);
        Assert.NotNull(alloc);

        // Second client fails
        var failAlloc = await store.TryAcquireOneAsync(licenseId, "sha256:fp2", "mch2", now, ttl);
        Assert.Null(failAlloc);

        // First client releases
        bool released = await store.TryReleaseAsync(alloc.LeaseId!, now);
        Assert.True(released);

        // Second client can now acquire seat
        var secondAlloc = await store.TryAcquireOneAsync(licenseId, "sha256:fp2", "mch2", now, ttl);
        Assert.NotNull(secondAlloc);
        Assert.Equal(1, secondAlloc.SeatNo);
    }

    [Fact]
    public async Task TryRelease_Clears_BorrowedUntil_For_Early_Return()
    {
        const string licenseId = "lic_test_borrow_release";
        await SeedLicenseAsync(licenseId, 1);

        await using var db = new SymbolonDbContext(_options);
        var store = new EfSeatStore(db);
        var now = DateTimeOffset.UtcNow;
        var ttl = TimeSpan.FromDays(7);

        // Acquire seat
        var alloc = await store.TryAcquireOneAsync(licenseId, "sha256:fp1", "mch1", now, ttl);
        Assert.NotNull(alloc);

        // Manually mark seat as borrowed for 7 days
        var seat = await db.Seats.FirstAsync(s => s.LeaseId == alloc.LeaseId);
        seat.BorrowedUntil = now.AddDays(7);
        await db.SaveChangesAsync();

        // Standard release fails because seat is borrowed and within borrow period (FLT-20, FLT-22)
        bool standardRelease = await store.TryReleaseAsync(alloc.LeaseId!, now.AddDays(1));
        Assert.False(standardRelease);

        // Client returns borrowed seat early via authenticated return (FLT-21)
        bool released = await store.TryReturnBorrowedSeatAsync(alloc.LeaseId!, now.AddDays(1));
        Assert.True(released);

        // Verify BorrowedUntil is cleared
        var updatedSeat = await db.Seats.FirstAsync(s => s.LicenseId == licenseId && s.SeatNo == 1);
        Assert.Null(updatedSeat.BorrowedUntil);
        Assert.Null(updatedSeat.LeaseId);

        // Another client can acquire immediately even though original borrow period hasn't elapsed
        var nextAlloc = await store.TryAcquireOneAsync(licenseId, "sha256:fp2", "mch2", now.AddDays(1), TimeSpan.FromHours(1));
        Assert.NotNull(nextAlloc);
        Assert.Equal(1, nextAlloc.SeatNo);
    }
}
