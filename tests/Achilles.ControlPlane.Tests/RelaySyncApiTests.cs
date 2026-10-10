using System.Net;
using System.Net.Http.Json;
using Achilles.ControlPlane.Models;
using Xunit;

namespace Achilles.ControlPlane.Tests;

public sealed class RelaySyncApiTests : IClassFixture<ControlPlaneFactory>
{
    private readonly HttpClient _client;

    public RelaySyncApiTests(ControlPlaneFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Can_Register_Relay_Request_Seat_Grant_And_Upload_Usage()
    {
        // 1. Create Tenant, Product, Policy, License with 10 seats
        var tenantRes = await _client.PostAsJsonAsync("/admin/v1/tenants", new CreateTenantDto($"relay-tenant-{Guid.NewGuid():N}", "Tenant"));
        var tenant = await tenantRes.Content.ReadFromJsonAsync<TenantDto>();

        var prodReq = new HttpRequestMessage(HttpMethod.Post, "/admin/v1/products")
        {
            Content = JsonContent.Create(new CreateProductDto($"prod-{Guid.NewGuid():N}", "Prod", ["linux"]))
        };
        prodReq.Headers.Add("X-Tenant-Id", tenant!.Id);
        var prodRes = await _client.SendAsync(prodReq);
        var product = await prodRes.Content.ReadFromJsonAsync<ProductDto>();

        var polRes = await _client.PostAsJsonAsync("/admin/v1/policies", new CreatePolicyDto
        {
            ProductId = product!.Id,
            Code = $"policy-{Guid.NewGuid():N}",
            Name = "Policy",
            MaxSeats = 10,
            LicenseModel = "floating"
        });
        var policy = await polRes.Content.ReadFromJsonAsync<PolicyDto>();

        var licRes = await _client.PostAsJsonAsync("/admin/v1/licenses", new CreateLicenseDto
        {
            PolicyId = policy!.Id,
            MaxSeats = 10
        });
        var license = await licRes.Content.ReadFromJsonAsync<LicenseResponseDto>();

        // 2. Register Relay
        var regReq = new HttpRequestMessage(HttpMethod.Post, "/relay/v1/register")
        {
            Content = JsonContent.Create(new RegisterRelayDto("Bratislava Branch Relay", "thumb_xyz"))
        };
        regReq.Headers.Add("X-Tenant-Id", tenant.Id);
        var regRes = await _client.SendAsync(regReq);
        Assert.Equal(HttpStatusCode.Created, regRes.StatusCode);
        var relay = await regRes.Content.ReadFromJsonAsync<RegisterRelayResponseDto>();
        Assert.NotNull(relay);
        Assert.NotEmpty(relay.ApiKey);

        // 3. Request Seat Grant of 4 seats for this relay
        var grantReq = new HttpRequestMessage(HttpMethod.Post, "/relay/v1/grants:request")
        {
            Content = JsonContent.Create(new RequestSeatGrantDto(
                relay.RelayId,
                license!.Id,
                4,
                30))
        };
        grantReq.Headers.Add("X-Relay-Api-Key", relay.ApiKey);
        var grantRes = await _client.SendAsync(grantReq);
        Assert.Equal(HttpStatusCode.OK, grantRes.StatusCode);
        var grant = await grantRes.Content.ReadFromJsonAsync<SeatGrantResponseDto>();
        Assert.NotNull(grant);
        Assert.NotEmpty(grant.Token);
        Assert.Equal(4, grant.SeatTo - grant.SeatFrom + 1);

        // 4. Upload Usage Batch from Relay
        var usageReq = new HttpRequestMessage(HttpMethod.Post, "/relay/v1/usage")
        {
            Content = JsonContent.Create(new RelayUsageBatchDto(
                relay.RelayId,
                [
                    new RelayAuditEventDto("checkout", license.Id, "sha256:fp1", DateTimeOffset.UtcNow, "Seat checkout at branch"),
                    new RelayAuditEventDto("renew", license.Id, "sha256:fp1", DateTimeOffset.UtcNow, "Heartbeat renewal")
                ]))
        };
        usageReq.Headers.Add("X-Relay-Api-Key", relay.ApiKey);
        var usageRes = await _client.SendAsync(usageReq);
        Assert.Equal(HttpStatusCode.OK, usageRes.StatusCode);
    }
}
