using System.Net;
using System.Net.Http.Json;
using Achilles.ControlPlane.Models;
using Achilles.ControlPlane.Webhooks;
using Achilles.Protocol;
using Xunit;

namespace Achilles.ControlPlane.Tests;

public sealed class WebhookTests : IClassFixture<ControlPlaneFactory>
{
    private readonly HttpClient _client;

    public WebhookTests(ControlPlaneFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public void ComputeSignature_Returns_Valid_HmacSha256_Hex()
    {
        string secret = "test-secret-key-123";
        long timestamp = 1774185600;
        string payload = "{\"test\":true}";

        string sig = WebhookDispatcher.ComputeSignature(secret, timestamp, payload);

        Assert.NotNull(sig);
        Assert.Equal(64, sig.Length); // 256 bits = 32 bytes = 64 hex chars
        Assert.Matches("^[0-9a-f]{64}$", sig);

        // Deterministic
        string sig2 = WebhookDispatcher.ComputeSignature(secret, timestamp, payload);
        Assert.Equal(sig, sig2);
    }

    [Fact]
    public async Task Webhook_Crud_Lifecycle_Works()
    {
        // 1. Create Webhook Subscription
        var createDto = new CreateWebhookDto("https://example.com/api/symbolon-wh", ["license.created", "seat.denied"], "custom-secret-key-999");
        var postRes = await _client.PostAsJsonAsync("/admin/v1/webhooks", createDto);
        Assert.Equal(HttpStatusCode.Created, postRes.StatusCode);

        var sub = await postRes.Content.ReadFromJsonAsync<WebhookSubscriptionDto>();
        Assert.NotNull(sub);
        Assert.Equal("https://example.com/api/symbolon-wh", sub.Url);
        Assert.True(sub.IsActive);
        Assert.Contains("license.created", sub.Events);

        // 2. Get All Webhooks
        var listRes = await _client.GetAsync("/admin/v1/webhooks");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var list = await listRes.Content.ReadFromJsonAsync<List<WebhookSubscriptionDto>>();
        Assert.NotNull(list);
        Assert.Contains(list, w => w.Id == sub.Id);

        // 3. Get By ID
        var getRes = await _client.GetAsync($"/admin/v1/webhooks/{sub.Id}");
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
        var fetched = await getRes.Content.ReadFromJsonAsync<WebhookSubscriptionDto>();
        Assert.NotNull(fetched);
        Assert.Equal(sub.Id, fetched.Id);

        // 4. Test Ping
        var pingRes = await _client.PostAsync($"/admin/v1/webhooks/{sub.Id}/test", null);
        Assert.Equal(HttpStatusCode.OK, pingRes.StatusCode);
        var pingResult = await pingRes.Content.ReadFromJsonAsync<WebhookTestResultDto>();
        Assert.NotNull(pingResult);

        // 5. Delete Webhook
        var delRes = await _client.DeleteAsync($"/admin/v1/webhooks/{sub.Id}");
        Assert.Equal(HttpStatusCode.OK, delRes.StatusCode);

        // 6. Verify Deleted
        var getAfterDel = await _client.GetAsync($"/admin/v1/webhooks/{sub.Id}");
        Assert.Equal(HttpStatusCode.NotFound, getAfterDel.StatusCode);
    }

    [Fact]
    public async Task License_Issuance_And_Seat_Denied_Trigger_Webhook_Deliveries()
    {
        // 1. Setup Tenant, Product, 1-seat Policy
        var tenantRes = await _client.PostAsJsonAsync("/admin/v1/tenants", new CreateTenantDto($"wh-ten-{Guid.NewGuid():N}", "Webhook Tenant"));
        var tenant = await tenantRes.Content.ReadFromJsonAsync<TenantDto>();
        Assert.NotNull(tenant);

        // 2. Register Webhook for all events under this tenant
        var createDto = new CreateWebhookDto("https://127.0.0.1:59999/webhook-sink", ["*"], "secret-wh-123");
        var subReq = new HttpRequestMessage(HttpMethod.Post, "/admin/v1/webhooks")
        {
            Content = JsonContent.Create(createDto)
        };
        subReq.Headers.Add("X-Tenant-Id", tenant.Id);
        var subRes = await _client.SendAsync(subReq);
        Assert.Equal(HttpStatusCode.Created, subRes.StatusCode);
        var sub = await subRes.Content.ReadFromJsonAsync<WebhookSubscriptionDto>();
        Assert.NotNull(sub);
        Assert.Equal(tenant.Id, sub.TenantId);

        var prodReq = new HttpRequestMessage(HttpMethod.Post, "/admin/v1/products")
        {
            Content = JsonContent.Create(new CreateProductDto($"wh-prod-{Guid.NewGuid():N}", "Webhook Product", ["windows"]))
        };
        prodReq.Headers.Add("X-Tenant-Id", tenant.Id);
        var prodRes = await _client.SendAsync(prodReq);
        var prod = await prodRes.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(prod);

        var polRes = await _client.PostAsJsonAsync("/admin/v1/policies", new CreatePolicyDto
        {
            ProductId = prod.Id,
            Code = "pol-1-seat",
            Name = "1 Seat Policy",
            MaxSeats = 1,
            LicenseModel = "floating"
        });
        var pol = await polRes.Content.ReadFromJsonAsync<PolicyDto>();
        Assert.NotNull(pol);

        // 3. Issue License -> Triggers license.created webhook
        var licRes = await _client.PostAsJsonAsync("/admin/v1/licenses", new CreateLicenseDto
        {
            PolicyId = pol.Id,
            CustomerRef = "Customer Webhook Test",
            MaxSeats = 1
        });
        Assert.Equal(HttpStatusCode.Created, licRes.StatusCode);
        var lic = await licRes.Content.ReadFromJsonAsync<LicenseResponseDto>();
        Assert.NotNull(lic);

        // 4. Checkout Seat 1 -> Success
        var chk1 = await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = lic.LicenseKey!,
            Quantity = 1,
            MachineId = "host-01",
            FingerprintComponents = new Dictionary<string, string> { ["os"] = "win11" }
        });
        Assert.Equal(HttpStatusCode.OK, chk1.StatusCode);

        // 5. Checkout Seat 2 -> Denied (409 Conflict) -> Triggers seat.denied webhook
        var chk2 = await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = lic.LicenseKey!,
            Quantity = 1,
            MachineId = "host-02",
            FingerprintComponents = new Dictionary<string, string> { ["os"] = "win11" }
        });
        Assert.Equal(HttpStatusCode.Conflict, chk2.StatusCode);

        // 6. Verify Webhook Deliveries recorded in DB
        var deliveriesRes = await _client.GetAsync($"/admin/v1/webhooks/{sub.Id}/deliveries");
        Assert.Equal(HttpStatusCode.OK, deliveriesRes.StatusCode);
        var deliveries = await deliveriesRes.Content.ReadFromJsonAsync<List<WebhookDeliveryDto>>();
        Assert.NotNull(deliveries);

        // Should have recorded attempts for license.created and seat.denied
        Assert.Contains(deliveries, d => d.EventType == "license.created");
        Assert.Contains(deliveries, d => d.EventType == "seat.denied");
    }
}
