using FluentAssertions;
using Symbolon.Domain.Tokens;
using Xunit;

namespace Symbolon.Domain.Tests;

public sealed class TokenEngineTests
{
    private sealed class InMemoryTokenStore : ITokenStore
    {
        private readonly Dictionary<string, TokenWalletModel> _wallets = new();
        private readonly List<TokenRateModel> _rates = new();
        private readonly Dictionary<string, TokenReservationModel> _reservations = new();
        private readonly List<TokenLedgerEntryModel> _ledger = new();
        private long _ledgerSeq = 1;

        public Task<TokenWalletModel> CreateWalletAsync(TokenWalletModel wallet, CancellationToken ct = default)
        {
            _wallets[wallet.Id] = wallet;
            return Task.FromResult(wallet);
        }

        public Task<TokenWalletModel?> GetWalletByIdAsync(string walletId, CancellationToken ct = default)
        {
            _wallets.TryGetValue(walletId, out var wallet);
            return Task.FromResult(wallet);
        }

        public Task<TokenWalletModel?> GetWalletByCodeAsync(string tenantId, string code, CancellationToken ct = default)
        {
            var wallet = _wallets.Values.FirstOrDefault(w =>
                string.Equals(w.TenantId, tenantId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(w.Code, code, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(wallet);
        }

        public Task<IReadOnlyList<TokenWalletModel>> ListWalletsAsync(string? tenantId, CancellationToken ct = default)
        {
            var list = _wallets.Values
                .Where(w => tenantId == null || string.Equals(w.TenantId, tenantId, StringComparison.OrdinalIgnoreCase))
                .ToList();
            return Task.FromResult<IReadOnlyList<TokenWalletModel>>(list);
        }

        public Task<TokenWalletModel> UpdateWalletAsync(TokenWalletModel wallet, CancellationToken ct = default)
        {
            _wallets[wallet.Id] = wallet;
            return Task.FromResult(wallet);
        }

        public Task<TokenRateModel> SetRateAsync(TokenRateModel rate, CancellationToken ct = default)
        {
            int idx = _rates.FindIndex(r =>
                r.TenantId == rate.TenantId &&
                r.ProductId == rate.ProductId &&
                string.Equals(r.FeatureCode, rate.FeatureCode, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                _rates[idx] = rate;
            }
            else
            {
                _rates.Add(rate);
            }
            return Task.FromResult(rate);
        }

        public Task<TokenRateModel?> GetRateAsync(string tenantId, string? productId, string featureCode, CancellationToken ct = default)
        {
            var rate = _rates.FirstOrDefault(r =>
                r.TenantId == tenantId &&
                (productId == null || r.ProductId == productId) &&
                string.Equals(r.FeatureCode, featureCode, StringComparison.OrdinalIgnoreCase));
            return Task.FromResult(rate);
        }

        public Task<IReadOnlyList<TokenRateModel>> ListRatesAsync(string? tenantId, string? productId, CancellationToken ct = default)
        {
            var list = _rates.Where(r =>
                (tenantId == null || r.TenantId == tenantId) &&
                (productId == null || r.ProductId == productId)).ToList();
            return Task.FromResult<IReadOnlyList<TokenRateModel>>(list);
        }

        public Task<TokenReservationModel?> GetReservationByIdAsync(string reservationId, CancellationToken ct = default)
        {
            _reservations.TryGetValue(reservationId, out var res);
            return Task.FromResult(res);
        }

        public Task<TokenReservationModel?> FindReservationByIdempotencyAsync(string idempotencyKey, CancellationToken ct = default)
        {
            var res = _reservations.Values.FirstOrDefault(r => r.IdempotencyKey == idempotencyKey);
            return Task.FromResult(res);
        }

        public Task<TokenReservationModel> SaveReservationAsync(TokenReservationModel reservation, CancellationToken ct = default)
        {
            _reservations[reservation.Id] = reservation;
            return Task.FromResult(reservation);
        }

        public Task<TokenLedgerEntryModel> AddLedgerEntryAsync(TokenLedgerEntryModel entry, CancellationToken ct = default)
        {
            var saved = entry with { Id = _ledgerSeq++ };
            _ledger.Add(saved);
            return Task.FromResult(saved);
        }

        public Task<IReadOnlyList<TokenLedgerEntryModel>> ListLedgerEntriesAsync(string? walletId, int limit, CancellationToken ct = default)
        {
            var list = _ledger.Where(l => walletId == null || l.WalletId == walletId)
                .OrderByDescending(l => l.Id)
                .Take(limit)
                .ToList();
            return Task.FromResult<IReadOnlyList<TokenLedgerEntryModel>>(list);
        }

        public Task<TokenWalletModel> UpdateWalletBalanceAtomicAsync(
            string walletId,
            decimal balanceDelta,
            decimal reservedDelta,
            decimal totalCreditsDelta,
            CancellationToken ct = default)
        {
            var w = _wallets[walletId];
            var updated = w with
            {
                Balance = w.Balance + balanceDelta,
                ReservedCredits = w.ReservedCredits + reservedDelta,
                TotalCredits = w.TotalCredits + totalCreditsDelta
            };
            _wallets[walletId] = updated;
            return Task.FromResult(updated);
        }

        public List<TokenLedgerEntryModel> AllLedger => _ledger;
    }

    [Fact]
    public async Task CreateWalletAsync_WithInitialCredits_CreatesWalletAndLedgerEntry()
    {
        var store = new InMemoryTokenStore();
        var engine = new TokenEngine(store);

        var wallet = await engine.CreateWalletAsync(new CreateWalletRequest(
            TenantId: "ten_01",
            Code: "CAD_TOKENS",
            Name: "Engineering CAD Tokens",
            InitialCredits: 500m,
            OverdraftLimit: 100m,
            ThresholdLowAlert: 50m));

        wallet.Should().NotBeNull();
        wallet.Code.Should().Be("CAD_TOKENS");
        wallet.Balance.Should().Be(500m);
        wallet.TotalCredits.Should().Be(500m);
        wallet.OverdraftLimit.Should().Be(100m);
        wallet.ReservedCredits.Should().Be(0m);

        store.AllLedger.Should().HaveCount(1);
        var entry = store.AllLedger[0];
        entry.WalletId.Should().Be(wallet.Id);
        entry.TransactionType.Should().Be("credit");
        entry.Amount.Should().Be(500m);
        entry.BalanceAfter.Should().Be(500m);
    }

    [Fact]
    public async Task CreditWalletAsync_IncreasesBalanceAndLogsLedger()
    {
        var store = new InMemoryTokenStore();
        var engine = new TokenEngine(store);

        var wallet = await engine.CreateWalletAsync(new CreateWalletRequest(
            TenantId: "ten_01",
            Code: "AI_TOKENS",
            Name: "AI Compute",
            InitialCredits: 100m));

        var credited = await engine.CreditWalletAsync(new CreditWalletRequest(
            WalletId: wallet.Id,
            Amount: 250m,
            Reason: "Monthly purchase",
            IdempotencyKey: "refill_01"));

        credited.Balance.Should().Be(350m);
        credited.TotalCredits.Should().Be(350m);

        store.AllLedger.Should().HaveCount(2);
        var refillEntry = store.AllLedger[1];
        refillEntry.Amount.Should().Be(250m);
        refillEntry.BalanceAfter.Should().Be(350m);
        refillEntry.IdempotencyKey.Should().Be("refill_01");
    }

    [Fact]
    public async Task ReserveTokensAsync_WhenBalanceSufficient_ReservesCreditsAndSetsTtl()
    {
        var store = new InMemoryTokenStore();
        var engine = new TokenEngine(store);

        var wallet = await engine.CreateWalletAsync(new CreateWalletRequest(
            TenantId: "ten_01",
            Code: "SIM_TOKENS",
            Name: "Simulation Pool",
            InitialCredits: 200m));

        var result = await engine.ReserveTokensAsync(new ReserveTokensRequest(
            WalletId: wallet.Id,
            FeatureCode: "FEA_NONLINEAR",
            EstimatedUnits: 20m,
            MachineId: "ws_42",
            ReservationTtl: TimeSpan.FromMinutes(1)));

        result.Success.Should().BeTrue();
        result.ReservationId.Should().NotBeNull();
        result.ReservedAmount.Should().Be(20m);
        result.AvailableBalance.Should().Be(180m);

        var updatedWallet = await store.GetWalletByIdAsync(wallet.Id);
        updatedWallet!.Balance.Should().Be(200m);
        updatedWallet.ReservedCredits.Should().Be(20m);
    }

    [Fact]
    public async Task ReserveTokensAsync_WhenBalanceExceeded_RejectsWithoutOverdraft()
    {
        var store = new InMemoryTokenStore();
        var engine = new TokenEngine(store);

        var wallet = await engine.CreateWalletAsync(new CreateWalletRequest(
            TenantId: "ten_01",
            Code: "SIM_TOKENS",
            Name: "Simulation Pool",
            InitialCredits: 10m,
            OverdraftLimit: 0m));

        var result = await engine.ReserveTokensAsync(new ReserveTokensRequest(
            WalletId: wallet.Id,
            FeatureCode: "FEA_SOLVER",
            EstimatedUnits: 50m));

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("Insufficient credits");
        result.ReservationId.Should().BeNull();

        var untouchedWallet = await store.GetWalletByIdAsync(wallet.Id);
        untouchedWallet!.Balance.Should().Be(10m);
        untouchedWallet.ReservedCredits.Should().Be(0m);
    }

    [Fact]
    public async Task ReserveTokensAsync_WithOverdraft_AllowsNegativeBalanceUpToLimit()
    {
        var store = new InMemoryTokenStore();
        var engine = new TokenEngine(store);

        var wallet = await engine.CreateWalletAsync(new CreateWalletRequest(
            TenantId: "ten_01",
            Code: "OVERDRAFT_POOL",
            Name: "Overdraft Enabled Pool",
            InitialCredits: 10m,
            OverdraftLimit: 50m));

        // Available balance is 10 + 50 = 60
        var result = await engine.ReserveTokensAsync(new ReserveTokensRequest(
            WalletId: wallet.Id,
            FeatureCode: "HEAVY_SOLVE",
            EstimatedUnits: 40m));

        result.Success.Should().BeTrue();
        result.ReservedAmount.Should().Be(40m);
        result.OverdraftRemaining.Should().Be(20m);

        // Second reservation of 25m would bring balance deficit beyond limit (need 25, only 20 left)
        var second = await engine.ReserveTokensAsync(new ReserveTokensRequest(
            WalletId: wallet.Id,
            FeatureCode: "HEAVY_SOLVE",
            EstimatedUnits: 25m));

        second.Success.Should().BeFalse();
        second.FailureReason.Should().Contain("Insufficient credits");
    }

    [Fact]
    public async Task ReserveTokensAsync_WithExistingActiveReservation_ReturnsExistingReservation()
    {
        var store = new InMemoryTokenStore();
        var engine = new TokenEngine(store);

        var wallet = await engine.CreateWalletAsync(new CreateWalletRequest(
            TenantId: "ten_01",
            Code: "CAD_TOKENS",
            Name: "CAD",
            InitialCredits: 100m));

        var r1 = await engine.ReserveTokensAsync(new ReserveTokensRequest(
            WalletId: wallet.Id,
            FeatureCode: "CAD_RENDER",
            EstimatedUnits: 10m,
            IdempotencyKey: "req_idem_123"));

        var r2 = await engine.ReserveTokensAsync(new ReserveTokensRequest(
            WalletId: wallet.Id,
            FeatureCode: "CAD_RENDER",
            EstimatedUnits: 10m,
            IdempotencyKey: "req_idem_123"));

        r1.Success.Should().BeTrue();
        r2.Success.Should().BeTrue();
        r2.ReservationId.Should().Be(r1.ReservationId);

        // Verify balance was only reserved once
        var w = await store.GetWalletByIdAsync(wallet.Id);
        w!.Balance.Should().Be(100m);
        w.ReservedCredits.Should().Be(10m);
    }

    [Fact]
    public async Task HeartbeatTokensAsync_UpdatesConsumedAndExtendsTtl()
    {
        var store = new InMemoryTokenStore();
        var engine = new TokenEngine(store);

        var wallet = await engine.CreateWalletAsync(new CreateWalletRequest(
            TenantId: "ten_01",
            Code: "GPU_TOKENS",
            Name: "GPU Computing",
            InitialCredits: 50m));

        var reserve = await engine.ReserveTokensAsync(new ReserveTokensRequest(
            WalletId: wallet.Id,
            FeatureCode: "GPU_CUDA",
            EstimatedUnits: 20m));

        var heartbeat = await engine.HeartbeatTokensAsync(new HeartbeatTokensRequest(
            ReservationId: reserve.ReservationId!,
            DeltaUnits: 5m));

        heartbeat.Success.Should().BeTrue();
        heartbeat.TotalConsumed.Should().Be(5m);
        heartbeat.RemainingReserved.Should().Be(15m);

        var resInStore = await store.GetReservationByIdAsync(reserve.ReservationId!);
        resInStore!.ConsumedAmount.Should().Be(5m);
        resInStore.ExpiresAt.Should().BeAfter(DateTimeOffset.UtcNow.AddMinutes(10));
    }

    [Fact]
    public async Task CommitTokensAsync_DeductsActualConsumedAndReleasesRemaining()
    {
        var store = new InMemoryTokenStore();
        var engine = new TokenEngine(store);

        var wallet = await engine.CreateWalletAsync(new CreateWalletRequest(
            TenantId: "ten_01",
            Code: "SIM_TOKENS",
            Name: "Sim",
            InitialCredits: 100m));

        // Reserve 30 credits (balance: 100, reserved: 30)
        var reserve = await engine.ReserveTokensAsync(new ReserveTokensRequest(
            WalletId: wallet.Id,
            FeatureCode: "SOLVER",
            EstimatedUnits: 30m));

        // Commit with 18 credits consumed (unused 12 released)
        var commit = await engine.CommitTokensAsync(new CommitTokensRequest(
            ReservationId: reserve.ReservationId!,
            ActualTotalUnits: 18m));

        commit.Success.Should().BeTrue();
        commit.ConsumedCredits.Should().Be(18m);
        commit.ReleasedCredits.Should().Be(12m);
        commit.FinalBalance.Should().Be(82m); // 100 - 18

        var updatedWallet = await store.GetWalletByIdAsync(wallet.Id);
        updatedWallet!.Balance.Should().Be(82m);
        updatedWallet.ReservedCredits.Should().Be(0m);

        var res = await store.GetReservationByIdAsync(reserve.ReservationId!);
        res!.Status.Should().Be("committed");

        // Verify consume ledger entry
        var consume = store.AllLedger.FirstOrDefault(l => l.TransactionType == "consume");
        consume.Should().NotBeNull();
        consume!.Amount.Should().Be(18m);
        consume.BalanceAfter.Should().Be(82m);
    }

    [Fact]
    public async Task RollbackTokensAsync_ReleasesAllReservedCredits()
    {
        var store = new InMemoryTokenStore();
        var engine = new TokenEngine(store);

        var wallet = await engine.CreateWalletAsync(new CreateWalletRequest(
            TenantId: "ten_01",
            Code: "SIM_TOKENS",
            Name: "Sim",
            InitialCredits: 100m));

        var reserve = await engine.ReserveTokensAsync(new ReserveTokensRequest(
            WalletId: wallet.Id,
            FeatureCode: "SOLVER",
            EstimatedUnits: 25m));

        // Rollback reservation
        var rollback = await engine.RollbackTokensAsync(new RollbackTokensRequest(
            ReservationId: reserve.ReservationId!,
            Reason: "Job cancelled by user"));

        rollback.Success.Should().BeTrue();
        rollback.ReleasedCredits.Should().Be(25m);
        rollback.CurrentBalance.Should().Be(100m);

        var updatedWallet = await store.GetWalletByIdAsync(wallet.Id);
        updatedWallet!.Balance.Should().Be(100m);
        updatedWallet.ReservedCredits.Should().Be(0m);

        var res = await store.GetReservationByIdAsync(reserve.ReservationId!);
        res!.Status.Should().Be("rolled_back");
    }

    [Fact]
    public async Task DynamicFeatureRates_UsesCalculatedRateWhenQuantitySpecified()
    {
        var store = new InMemoryTokenStore();
        var engine = new TokenEngine(store);

        // Set rate for FEA_MESH to 2.5 credits per unit
        await engine.SetRateAsync(new SetRateRequest(
            TenantId: "ten_01",
            FeatureCode: "FEA_MESH",
            RatePerMinute: 0m,
            RatePerUnit: 2.5m,
            Description: "Mesh generation rate"));

        var wallet = await engine.CreateWalletAsync(new CreateWalletRequest(
            TenantId: "ten_01",
            Code: "CAD_ENGINEERING",
            Name: "CAD",
            InitialCredits: 100m));

        // Request 10 units of FEA_MESH => 10 * 2.5 = 25 credits reserved
        var reserve = await engine.ReserveTokensAsync(new ReserveTokensRequest(
            WalletId: wallet.Id,
            FeatureCode: "FEA_MESH",
            EstimatedUnits: 10m));

        reserve.Success.Should().BeTrue();
        reserve.ReservedAmount.Should().Be(25m);
        reserve.AvailableBalance.Should().Be(75m);
    }

    [Fact]
    public async Task ThresholdLowAlert_TriggersWhenBalanceFallsBelowThreshold()
    {
        var store = new InMemoryTokenStore();
        var engine = new TokenEngine(store);

        var wallet = await engine.CreateWalletAsync(new CreateWalletRequest(
            TenantId: "ten_01",
            Code: "ALERT_TEST",
            Name: "Alert Test",
            InitialCredits: 50m,
            ThresholdLowAlert: 20m));

        // Reserve 35 units
        var reserve = await engine.ReserveTokensAsync(new ReserveTokensRequest(
            WalletId: wallet.Id,
            FeatureCode: "GENERIC_TASK",
            EstimatedUnits: 35m));

        reserve.Success.Should().BeTrue();

        // Commit 35 units -> Balance drops from 50 to 15, which is <= 20
        var commit = await engine.CommitTokensAsync(new CommitTokensRequest(
            ReservationId: reserve.ReservationId!,
            ActualTotalUnits: 35m));

        commit.Success.Should().BeTrue();
        commit.FinalBalance.Should().Be(15m);
        commit.IsLowBalanceAlert.Should().BeTrue();
    }
}
