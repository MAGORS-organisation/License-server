using System.Net;
using System.Net.Http.Json;
using Symbolon.ControlPlane.Models;
using Symbolon.Protocol;
using Xunit;

namespace Symbolon.ControlPlane.Tests;

public sealed class AdvancedLicensingTests : IClassFixture<ControlPlaneFactory>
{
    private readonly HttpClient _client;

    public AdvancedLicensingTests(ControlPlaneFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<(string LicenseId, string LicenseKey)> CreateTestLicenseAsync(string licenseModel = "floating", int seats = 1)
    {
        var tenantRes = await _client.PostAsJsonAsync("/admin/v1/tenants", new CreateTenantDto($"tenant-{Guid.NewGuid():N}", "Tenant"));
        var tenant = await tenantRes.Content.ReadFromJsonAsync<TenantDto>();

        var prodReq = new HttpRequestMessage(HttpMethod.Post, "/admin/v1/products")
        {
            Content = JsonContent.Create(new CreateProductDto($"prod-{Guid.NewGuid():N}", "Product", ["windows"]))
        };
        prodReq.Headers.Add("X-Tenant-Id", tenant!.Id);
        var prodRes = await _client.SendAsync(prodReq);
        var product = await prodRes.Content.ReadFromJsonAsync<ProductDto>();

        var polRes = await _client.PostAsJsonAsync("/admin/v1/policies", new CreatePolicyDto
        {
            ProductId = product!.Id,
            Code = $"policy-{Guid.NewGuid():N}",
            Name = "Policy",
            MaxSeats = seats,
            LicenseModel = licenseModel
        });
        var policy = await polRes.Content.ReadFromJsonAsync<PolicyDto>();

        var licRes = await _client.PostAsJsonAsync("/admin/v1/licenses", new CreateLicenseDto
        {
            PolicyId = policy!.Id,
            MaxSeats = seats
        });
        var license = await licRes.Content.ReadFromJsonAsync<LicenseResponseDto>();

        return (license!.Id, license.LicenseKey!);
    }

    [Fact]
    public async Task NamedUser_Enforcement_And_Assignment_Works()
    {
        var (licId, key) = await CreateTestLicenseAsync(licenseModel: "named-user", seats: 2);
        var fp = new Dictionary<string, string> { ["machineId"] = "workstation-01" };

        // 1. Checkout without userId is rejected with 403 Forbidden
        var noUserRes = await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = key,
            FingerprintComponents = fp
        });
        Assert.Equal(HttpStatusCode.Forbidden, noUserRes.StatusCode);

        // 2. Checkout with unassigned user is rejected with 403 Forbidden
        var unauthRes = await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = key,
            UserId = "bob@example.com",
            FingerprintComponents = fp
        });
        Assert.Equal(HttpStatusCode.Forbidden, unauthRes.StatusCode);

        // 3. Admin assigns user "alice@example.com" with group "Devs"
        var assignRes = await _client.PostAsJsonAsync($"/admin/v1/licenses/{licId}/users", new AssignLicenseUserDto("alice@example.com", "Devs"));
        Assert.Equal(HttpStatusCode.Created, assignRes.StatusCode);

        // 4. Admin lists assigned users
        var listRes = await _client.GetAsync($"/admin/v1/licenses/{licId}/users");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var users = await listRes.Content.ReadFromJsonAsync<List<LicenseUserDto>>();
        Assert.NotNull(users);
        Assert.Contains(users, u => u.UserId == "alice@example.com" && u.GroupName == "Devs");

        // 5. Checkout with authorized user succeeds
        var chkUserRes = await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = key,
            UserId = "alice@example.com",
            FingerprintComponents = fp
        });
        Assert.Equal(HttpStatusCode.OK, chkUserRes.StatusCode);
        var leaseUser = await chkUserRes.Content.ReadFromJsonAsync<CheckoutResponseDto>();
        Assert.NotNull(leaseUser);
        Assert.NotEmpty(leaseUser.Token);

        // 6. Checkout matching group name succeeds
        var fp2 = new Dictionary<string, string> { ["machineId"] = "workstation-02" };
        var chkGroupRes = await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = key,
            UserId = "Devs",
            FingerprintComponents = fp2
        });
        Assert.Equal(HttpStatusCode.OK, chkGroupRes.StatusCode);

        // 7. Admin removes user
        var delRes = await _client.DeleteAsync($"/admin/v1/licenses/{licId}/users/alice@example.com");
        Assert.Equal(HttpStatusCode.OK, delRes.StatusCode);
    }

    [Fact]
    public async Task SeatQueueing_Lifecycle_And_Promotion_Works()
    {
        var (_, key) = await CreateTestLicenseAsync(licenseModel: "floating", seats: 1);
        var fp1 = new Dictionary<string, string> { ["host"] = "host1" };
        var fp2 = new Dictionary<string, string> { ["host"] = "host2" };

        // 1. Client 1 checks out the only available seat
        var chk1 = await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = key,
            FingerprintComponents = fp1
        });
        Assert.Equal(HttpStatusCode.OK, chk1.StatusCode);
        var lease1 = await chk1.Content.ReadFromJsonAsync<CheckoutResponseDto>();
        Assert.NotNull(lease1);

        // 2. Client 2 attempts checkout with AllowQueue = true -> 202 Accepted
        var chk2 = await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = key,
            FingerprintComponents = fp2,
            AllowQueue = true
        });
        Assert.Equal(HttpStatusCode.Accepted, chk2.StatusCode);
        var queued = await chk2.Content.ReadFromJsonAsync<QueuedResponseDto>();
        Assert.NotNull(queued);
        Assert.NotEmpty(queued.Ticket);

        // 3. Client 2 polls queue -> status = waiting, position = 1
        var poll1 = await _client.GetAsync($"/v1/queue/{queued.Ticket}");
        Assert.Equal(HttpStatusCode.OK, poll1.StatusCode);
        var status1 = await poll1.Content.ReadFromJsonAsync<QueueStatusResponseDto>();
        Assert.NotNull(status1);
        Assert.Equal("waiting", status1.Status);
        Assert.Equal(1, status1.Position);

        // 4. Client 1 releases seat -> queue manager automatically promotes waiting ticket
        var rel = await _client.DeleteAsync($"/v1/leases/{lease1.LeaseId}");
        Assert.Equal(HttpStatusCode.OK, rel.StatusCode);

        // 5. Client 2 polls queue -> status = ready, Token present
        var poll2 = await _client.GetAsync($"/v1/queue/{queued.Ticket}");
        Assert.Equal(HttpStatusCode.OK, poll2.StatusCode);
        var status2 = await poll2.Content.ReadFromJsonAsync<QueueStatusResponseDto>();
        Assert.NotNull(status2);
        Assert.Equal("ready", status2.Status);
        Assert.NotNull(status2.Token);
        Assert.NotEmpty(status2.Token);
        Assert.NotNull(status2.LeaseId);
        Assert.NotEmpty(status2.LeaseId);

        // 6. Test queue cancellation
        var chk3 = await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = key,
            FingerprintComponents = fp1,
            AllowQueue = true
        });
        Assert.Equal(HttpStatusCode.Accepted, chk3.StatusCode);
        var queued3 = await chk3.Content.ReadFromJsonAsync<QueuedResponseDto>();
        Assert.NotNull(queued3);

        var cancelRes = await _client.DeleteAsync($"/v1/queue/{queued3.Ticket}");
        Assert.Equal(HttpStatusCode.OK, cancelRes.StatusCode);

        var pollCancelled = await _client.GetAsync($"/v1/queue/{queued3.Ticket}");
        Assert.Equal(HttpStatusCode.Gone, pollCancelled.StatusCode);
    }

    [Fact]
    public async Task MeteredQuota_Consumption_And_Balance_Tracking_Works()
    {
        var (licId, key) = await CreateTestLicenseAsync(seats: 5);

        // 1. Admin configures quota of 100 units for "render-minutes"
        var setQuotaRes = await _client.PostAsJsonAsync($"/admin/v1/licenses/{licId}/quotas", new SetLicenseQuotaDto("render-minutes", 100));
        Assert.Equal(HttpStatusCode.Created, setQuotaRes.StatusCode);
        var quotaAdmin = await setQuotaRes.Content.ReadFromJsonAsync<LicenseQuotaAdminDto>();
        Assert.NotNull(quotaAdmin);
        Assert.Equal(100, quotaAdmin.TotalUnits);
        Assert.Equal(100, quotaAdmin.RemainingUnits);

        // 2. Consume 35 units
        var consumeRes1 = await _client.PostAsJsonAsync("/v1/entitlements/consume", new ConsumeQuotaRequestDto
        {
            LicenseKey = key,
            EntitlementCode = "render-minutes",
            Units = 35
        });
        Assert.Equal(HttpStatusCode.OK, consumeRes1.StatusCode);
        var balance1 = await consumeRes1.Content.ReadFromJsonAsync<QuotaBalanceDto>();
        Assert.NotNull(balance1);
        Assert.Equal(35, balance1.ConsumedUnits);
        Assert.Equal(65, balance1.RemainingUnits);

        // 3. Query balance endpoint
        var balRes = await _client.GetAsync($"/v1/entitlements/balance?licenseKey={Uri.EscapeDataString(key)}");
        Assert.Equal(HttpStatusCode.OK, balRes.StatusCode);
        var balances = await balRes.Content.ReadFromJsonAsync<List<QuotaBalanceDto>>();
        Assert.NotNull(balances);
        var quotaBalance = Assert.Single(balances);
        Assert.Equal("render-minutes", quotaBalance.EntitlementCode);
        Assert.Equal(65, quotaBalance.RemainingUnits);

        // 4. Consume another 50 units (remaining: 15)
        var consumeRes2 = await _client.PostAsJsonAsync("/v1/entitlements/consume", new ConsumeQuotaRequestDto
        {
            LicenseKey = key,
            EntitlementCode = "render-minutes",
            Units = 50
        });
        Assert.Equal(HttpStatusCode.OK, consumeRes2.StatusCode);
        var balance2 = await consumeRes2.Content.ReadFromJsonAsync<QuotaBalanceDto>();
        Assert.NotNull(balance2);
        Assert.Equal(85, balance2.ConsumedUnits);
        Assert.Equal(15, balance2.RemainingUnits);

        // 5. Overdraft attempt: consume 20 units when only 15 remain -> 409 Conflict
        var overdraftRes = await _client.PostAsJsonAsync("/v1/entitlements/consume", new ConsumeQuotaRequestDto
        {
            LicenseKey = key,
            EntitlementCode = "render-minutes",
            Units = 20
        });
        Assert.Equal(HttpStatusCode.Conflict, overdraftRes.StatusCode);
    }
}
