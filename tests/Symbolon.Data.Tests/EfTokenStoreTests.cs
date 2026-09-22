using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Symbolon.Data.Entities;
using Symbolon.Data.Stores;
using Symbolon.Domain.Tokens;
using Xunit;

namespace Symbolon.Data.Tests;

public sealed class EfTokenStoreTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<SymbolonDbContext> _options;

    public EfTokenStoreTests()
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

    private async Task<string> SeedTenantAsync()
    {
        await using var db = new SymbolonDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        string tenantId = $"ten_{Guid.NewGuid():N}";
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Slug = $"tenant-{Guid.NewGuid():N}",
            Name = "Test Tenant",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        return tenantId;
    }

    [Fact]
    public async Task CreateAndRetrieveWallet_WorksSuccessfully()
    {
        var tenantId = await SeedTenantAsync();
        await using var db = new SymbolonDbContext(_options);
        var store = new EfTokenStore(db);

        var wallet = new TokenWalletModel(
            Id: $"wal_{Guid.NewGuid():N}",
            TenantId: tenantId,
            LicenseId: null,
            Code: "TEST_WALLET",
            Name: "Test Wallet",
            TotalCredits: 500m,
            Balance: 500m,
            ReservedCredits: 0m,
            OverdraftLimit: 50m,
            State: "active",
            ExpiresAt: null,
            ThresholdLowAlert: 20m,
            CreatedAt: DateTimeOffset.UtcNow,
            LastRefillAt: DateTimeOffset.UtcNow);

        var created = await store.CreateWalletAsync(wallet);
        created.Should().NotBeNull();
        created.Id.Should().Be(wallet.Id);

        var fetchedById = await store.GetWalletByIdAsync(wallet.Id);
        fetchedById.Should().NotBeNull();
        fetchedById!.Code.Should().Be("TEST_WALLET");
        fetchedById.Balance.Should().Be(500m);

        var fetchedByCode = await store.GetWalletByCodeAsync(tenantId, "TEST_WALLET");
        fetchedByCode.Should().NotBeNull();
        fetchedByCode!.Id.Should().Be(wallet.Id);

        var list = await store.ListWalletsAsync(tenantId);
        list.Should().ContainSingle(w => w.Id == wallet.Id);
    }

    [Fact]
    public async Task UpdateWalletBalanceAtomicAsync_UpdatesBalancesCorrectly()
    {
        var tenantId = await SeedTenantAsync();
        await using var db = new SymbolonDbContext(_options);
        var store = new EfTokenStore(db);

        var wallet = new TokenWalletModel(
            Id: $"wal_{Guid.NewGuid():N}",
            TenantId: tenantId,
            LicenseId: null,
            Code: "ATOMIC_WALLET",
            Name: "Atomic Test Wallet",
            TotalCredits: 100m,
            Balance: 100m,
            ReservedCredits: 0m,
            OverdraftLimit: 0m,
            State: "active",
            ExpiresAt: null,
            ThresholdLowAlert: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastRefillAt: null);

        await store.CreateWalletAsync(wallet);

        // Reserve 25 credits
        var afterReserve = await store.UpdateWalletBalanceAtomicAsync(wallet.Id, balanceDelta: 0, reservedDelta: 25m, totalCreditsDelta: 0);
        afterReserve.Balance.Should().Be(100m);
        afterReserve.ReservedCredits.Should().Be(25m);

        // Commit 15 credits: balanceDelta = -15, reservedDelta = -25
        var afterCommit = await store.UpdateWalletBalanceAtomicAsync(wallet.Id, balanceDelta: -15m, reservedDelta: -25m, totalCreditsDelta: 0);
        afterCommit.Balance.Should().Be(85m);
        afterCommit.ReservedCredits.Should().Be(0m);

        // Credit 50 credits
        var afterCredit = await store.UpdateWalletBalanceAtomicAsync(wallet.Id, balanceDelta: 50m, reservedDelta: 0, totalCreditsDelta: 50m);
        afterCredit.Balance.Should().Be(135m);
        afterCredit.TotalCredits.Should().Be(150m);
    }

    [Fact]
    public async Task Rates_And_Reservations_And_Ledger_PersistCorrectly()
    {
        var tenantId = await SeedTenantAsync();
        await using var db = new SymbolonDbContext(_options);
        var store = new EfTokenStore(db);

        // 1. Set and get rate
        var rate = new TokenRateModel(
            Id: $"rate_{Guid.NewGuid():N}",
            TenantId: tenantId,
            ProductId: null,
            FeatureCode: "RENDER_4K",
            RatePerMinute: 2.0m,
            RatePerUnit: 10.0m,
            Description: "4K Rendering rate",
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

        await store.SetRateAsync(rate);

        var fetchedRate = await store.GetRateAsync(tenantId, null, "RENDER_4K");
        fetchedRate.Should().NotBeNull();
        fetchedRate!.RatePerUnit.Should().Be(10.0m);

        // 2. Create wallet & reservation
        var walletId = $"wal_{Guid.NewGuid():N}";
        await store.CreateWalletAsync(new TokenWalletModel(
            Id: walletId,
            TenantId: tenantId,
            LicenseId: null,
            Code: "RATE_WALLET",
            Name: "Rate Wallet",
            TotalCredits: 50m,
            Balance: 50m,
            ReservedCredits: 0m,
            OverdraftLimit: 0m,
            State: "active",
            ExpiresAt: null,
            ThresholdLowAlert: null,
            CreatedAt: DateTimeOffset.UtcNow,
            LastRefillAt: null));

        var reservation = new TokenReservationModel(
            Id: $"res_{Guid.NewGuid():N}",
            WalletId: walletId,
            FeatureCode: "RENDER_4K",
            ReservedAmount: 20m,
            ConsumedAmount: 0m,
            MachineId: "ws_render",
            ClientRef: "job_101",
            Status: "pending",
            IdempotencyKey: "idem_job_101",
            ExpiresAt: DateTimeOffset.UtcNow.AddMinutes(30),
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

        await store.SaveReservationAsync(reservation);

        var fetchedRes = await store.GetReservationByIdAsync(reservation.Id);
        fetchedRes.Should().NotBeNull();
        fetchedRes!.ReservedAmount.Should().Be(20m);

        var fetchedByIdem = await store.FindReservationByIdempotencyAsync("idem_job_101");
        fetchedByIdem.Should().NotBeNull();
        fetchedByIdem!.Id.Should().Be(reservation.Id);

        // 3. Ledger entries
        await store.AddLedgerEntryAsync(new TokenLedgerEntryModel(
            Id: 0,
            WalletId: walletId,
            ReservationId: reservation.Id,
            TransactionType: "reserve",
            Amount: 20m,
            BalanceAfter: 50m,
            FeatureCode: "RENDER_4K",
            IdempotencyKey: "idem_job_101",
            Timestamp: DateTimeOffset.UtcNow,
            MetadataJson: "{}"));

        var ledgerList = await store.ListLedgerEntriesAsync(walletId, 10);
        ledgerList.Should().HaveCount(1);
        ledgerList[0].TransactionType.Should().Be("reserve");
    }
}
