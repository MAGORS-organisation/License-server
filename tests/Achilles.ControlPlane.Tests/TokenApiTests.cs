using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Achilles.ControlPlane.Models;
using Achilles.Domain.Tokens;
using Xunit;

namespace Achilles.ControlPlane.Tests;

public sealed class TokenApiTests : IClassFixture<ControlPlaneFactory>
{
    private readonly HttpClient _client;

    public TokenApiTests(ControlPlaneFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<string> CreateTestTenantAsync()
    {
        var res = await _client.PostAsJsonAsync("/admin/v1/tenants", new CreateTenantDto($"tenant-{Guid.NewGuid():N}", "Tenant for Tokens"));
        res.EnsureSuccessStatusCode();
        var tenant = await res.Content.ReadFromJsonAsync<TenantDto>();
        return tenant!.Id;
    }

    [Fact]
    public async Task Token_Lifecycle_FullApiFlow_WorksEndToEnd()
    {
        var tenantId = await CreateTestTenantAsync();

        // 1. Configure Feature Rate
        var rateReq = new SetRateRequest(
            TenantId: tenantId,
            FeatureCode: "CAD_FEA_SOLVER",
            RatePerMinute: 1.5m,
            RatePerUnit: 5.0m,
            Description: "Nonlinear FEA solver rate");

        var rateRes = await _client.PostAsJsonAsync("/admin/v1/tokens/rates", rateReq);
        rateRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var rate = await rateRes.Content.ReadFromJsonAsync<TokenRateModel>();
        rate.Should().NotBeNull();
        rate!.FeatureCode.Should().Be("CAD_FEA_SOLVER");

        // 2. Create Token Wallet
        var createWalletReq = new CreateWalletRequest(
            TenantId: tenantId,
            Code: "ENG_POOL",
            Name: "Engineering Simulation Pool",
            InitialCredits: 100m,
            OverdraftLimit: 50m,
            ThresholdLowAlert: 20m);

        var createWalletRes = await _client.PostAsJsonAsync("/admin/v1/tokens/wallets", createWalletReq);
        createWalletRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var wallet = await createWalletRes.Content.ReadFromJsonAsync<TokenWalletModel>();
        wallet.Should().NotBeNull();
        wallet!.Balance.Should().Be(100m);

        // 3. Query Balance
        var balanceRes = await _client.GetAsync($"/v1/tokens/wallets/{wallet.Id}/balance");
        balanceRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var balance = await balanceRes.Content.ReadFromJsonAsync<TokenWalletBalanceDto>();
        balance.Should().NotBeNull();
        balance!.Balance.Should().Be(100m);
        balance.AvailableCredits.Should().Be(150m); // 100 + 50 overdraft

        // 4. Reserve tokens (10 units * 5.0 per unit = 50 credits)
        var reserveReq = new ReserveTokensRequest(
            WalletId: wallet.Id,
            FeatureCode: "CAD_FEA_SOLVER",
            EstimatedUnits: 10m,
            MachineId: "node-cluster-01",
            ClientRef: "job-run-777",
            IdempotencyKey: "idem_reserve_777");

        var reserveRes = await _client.PostAsJsonAsync("/v1/tokens/reserve", reserveReq);
        reserveRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var reserveResult = await reserveRes.Content.ReadFromJsonAsync<ReserveTokensResult>();
        reserveResult.Should().NotBeNull();
        reserveResult!.Success.Should().BeTrue();
        reserveResult.ReservedAmount.Should().Be(50m);
        reserveResult.ReservationId.Should().NotBeNull();

        // 5. Send Heartbeat
        var heartbeatReq = new HeartbeatTokensRequest(
            ReservationId: reserveResult.ReservationId!,
            DeltaUnits: 2m);

        var heartbeatRes = await _client.PostAsJsonAsync("/v1/tokens/heartbeat", heartbeatReq);
        heartbeatRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var heartbeatResult = await heartbeatRes.Content.ReadFromJsonAsync<HeartbeatTokensResult>();
        heartbeatResult.Should().NotBeNull();
        heartbeatResult!.Success.Should().BeTrue();
        heartbeatResult.TotalConsumed.Should().Be(10m); // 2 units * 5.0

        // 6. Commit Reservation (Actual total units: 6 * 5.0 = 30 credits consumed, 20 released)
        var commitReq = new CommitTokensRequest(
            ReservationId: reserveResult.ReservationId!,
            ActualTotalUnits: 6m);

        var commitRes = await _client.PostAsJsonAsync("/v1/tokens/commit", commitReq);
        commitRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var commitResult = await commitRes.Content.ReadFromJsonAsync<CommitTokensResult>();
        commitResult.Should().NotBeNull();
        commitResult!.Success.Should().BeTrue();
        commitResult.ConsumedCredits.Should().Be(30m);
        commitResult.ReleasedCredits.Should().Be(20m);
        commitResult.FinalBalance.Should().Be(70m); // 100 - 30

        // 7. Refill / Credit Wallet
        var creditReq = new
        {
            Amount = 100m,
            Reason = "Quarterly top-up",
            IdempotencyKey = "refill_q3"
        };

        var creditRes = await _client.PostAsJsonAsync($"/admin/v1/tokens/wallets/{wallet.Id}/credit", creditReq);
        creditRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var creditedWallet = await creditRes.Content.ReadFromJsonAsync<TokenWalletModel>();
        creditedWallet.Should().NotBeNull();
        creditedWallet!.Balance.Should().Be(170m);

        // 8. Rollback Scenario
        var reserveForRollback = await _client.PostAsJsonAsync("/v1/tokens/reserve", new ReserveTokensRequest(
            WalletId: wallet.Id,
            FeatureCode: "CAD_FEA_SOLVER",
            EstimatedUnits: 4m)); // 4 * 5.0 = 20 credits

        var resRollbackData = await reserveForRollback.Content.ReadFromJsonAsync<ReserveTokensResult>();
        resRollbackData!.Success.Should().BeTrue();

        var rollbackRes = await _client.PostAsJsonAsync("/v1/tokens/rollback", new RollbackTokensRequest(
            ReservationId: resRollbackData.ReservationId!,
            Reason: "Simulation converged early"));

        rollbackRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var rollbackData = await rollbackRes.Content.ReadFromJsonAsync<RollbackTokensResult>();
        rollbackData.Should().NotBeNull();
        rollbackData!.Success.Should().BeTrue();
        rollbackData.ReleasedCredits.Should().Be(20m);
        rollbackData.CurrentBalance.Should().Be(170m);

        // 9. Verify Ledger Entries
        var ledgerRes = await _client.GetAsync($"/admin/v1/tokens/ledger?walletId={wallet.Id}");
        ledgerRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var ledgerEntries = await ledgerRes.Content.ReadFromJsonAsync<List<TokenLedgerEntryModel>>();
        ledgerEntries.Should().NotBeNull();
        ledgerEntries!.Should().NotBeEmpty();
    }
}
