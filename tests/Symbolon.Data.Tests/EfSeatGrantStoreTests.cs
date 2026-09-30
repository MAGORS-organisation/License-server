using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Symbolon.Data.Entities;
using Symbolon.Data.Stores;
using Symbolon.Domain.Grants;
using Xunit;

namespace Symbolon.Data.Tests;

public sealed class EfSeatGrantStoreTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<SymbolonDbContext> _options;

    public EfSeatGrantStoreTests()
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

    private static async Task SeedPrerequisitesAsync(SymbolonDbContext db, string licenseId, string relayId)
    {
        var tenant = new Tenant
        {
            Id = "ten_01",
            Slug = "acme",
            Name = "Acme Corp",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Tenants.Add(tenant);

        var product = new Product
        {
            Id = "prd_01",
            TenantId = tenant.Id,
            Code = "cad",
            Name = "CAD",
            PlatformsJson = "[\"win\"]",
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Products.Add(product);

        var policy = new Policy
        {
            Id = "pol_01",
            TenantId = tenant.Id,
            ProductId = product.Id,
            Name = "Floating"
        };
        db.Policies.Add(policy);

        var license = new LicenseEntity
        {
            Id = licenseId,
            TenantId = tenant.Id,
            PolicyId = policy.Id,
            KeyHash = "hash001",
            State = "active",
            MaxSeats = 20,
            IssuedAt = DateTimeOffset.UtcNow,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.Licenses.Add(license);

        var relay = new RelayEntity
        {
            Id = relayId,
            TenantId = tenant.Id,
            Name = "Factory Relay 1"
        };
        db.Relays.Add(relay);

        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task SaveGrantAsync_PersistsAndCanBeRetrieved()
    {
        await using var db = new SymbolonDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        string licId = "lic_01";
        string rlyId = "rly_01";
        await SeedPrerequisitesAsync(db, licId, rlyId);

        var store = new EfSeatGrantStore(db);
        var now = DateTimeOffset.UtcNow;

        var grant = new SeatGrantRecord(
            Id: "gnt_01",
            LicenseId: licId,
            RelayId: rlyId,
            Seats: 5,
            SeatFrom: 0,
            SeatTo: 4,
            Seq: 1,
            Supersedes: null,
            NotBefore: now.AddHours(-1),
            NotAfter: now.AddHours(23),
            RevokedAt: null,
            Document: "-----BEGIN SYMBOLON SEAT GRANT-----..."
        );

        await store.SaveGrantAsync(grant);

        var retrieved = await store.GetGrantByIdAsync("gnt_01");
        retrieved.Should().NotBeNull();
        retrieved!.Seats.Should().Be(5);
        retrieved.SeatFrom.Should().Be(0);
        retrieved.SeatTo.Should().Be(4);
        retrieved.Seq.Should().Be(1);

        long highestSeq = await store.GetHighestSeqAsync(licId, rlyId);
        highestSeq.Should().Be(1);
    }

    [Fact]
    public async Task SaveGrantAsync_SupersedesOlderSeq_MarksPreviousRevoked()
    {
        await using var db = new SymbolonDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        string licId = "lic_01";
        string rlyId = "rly_01";
        await SeedPrerequisitesAsync(db, licId, rlyId);

        var store = new EfSeatGrantStore(db);
        var now = DateTimeOffset.UtcNow;

        var grant1 = new SeatGrantRecord(
            Id: "gnt_01",
            LicenseId: licId,
            RelayId: rlyId,
            Seats: 5,
            SeatFrom: 0,
            SeatTo: 4,
            Seq: 1,
            Supersedes: null,
            NotBefore: now.AddHours(-2),
            NotAfter: now.AddHours(22),
            RevokedAt: null,
            Document: "grant-1"
        );
        await store.SaveGrantAsync(grant1);

        // Grant 2 supersedes seq 1 (GNT-9)
        var grant2 = new SeatGrantRecord(
            Id: "gnt_02",
            LicenseId: licId,
            RelayId: rlyId,
            Seats: 5,
            SeatFrom: 0,
            SeatTo: 4,
            Seq: 2,
            Supersedes: 1,
            NotBefore: now,
            NotAfter: now.AddHours(24),
            RevokedAt: null,
            Document: "grant-2"
        );
        await store.SaveGrantAsync(grant2);

        var g1 = await store.GetGrantByIdAsync("gnt_01");
        g1.Should().NotBeNull();
        g1!.RevokedAt.Should().BeCloseTo(now, TimeSpan.FromSeconds(1)); // Superseded!

        var active = await store.GetActiveGrantsForLicenseAsync(licId, now);
        active.Should().HaveCount(1);
        active[0].Id.Should().Be("gnt_02");

        long highestSeq = await store.GetHighestSeqAsync(licId, rlyId);
        highestSeq.Should().Be(2);
    }

    [Fact]
    public async Task AntiReplayNonce_FLT34_TracksSeenNonces()
    {
        await using var db = new SymbolonDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        var store = new EfSeatGrantStore(db);
        var now = DateTimeOffset.UtcNow;
        string nonce = "nonce_random_12345";

        bool seenBefore = await store.HasNonceBeenSeenAsync(nonce);
        seenBefore.Should().BeFalse();

        await store.RecordNonceAsync(nonce, "rly_01", now, now.AddHours(1));

        bool seenAfter = await store.HasNonceBeenSeenAsync(nonce);
        seenAfter.Should().BeTrue();
    }
}
