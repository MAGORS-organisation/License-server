using System.Net;
using System.Net.Http.Json;
using Achilles.ControlPlane.Models;
using Achilles.Crypto;
using Achilles.Protocol;
using Xunit;

namespace Achilles.ControlPlane.Tests;

public sealed class PublicApiTests : IClassFixture<ControlPlaneFactory>
{
    private readonly HttpClient _client;

    public PublicApiTests(ControlPlaneFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<(string LicenseId, string LicenseKey)> CreateTestLicenseAsync(int seats = 1)
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
            LicenseModel = "floating"
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
    public async Task Checkout_Renew_And_Release_Flow_Works()
    {
        var (_, key) = await CreateTestLicenseAsync(seats: 1);

        var fp1 = new Dictionary<string, string> { ["machineId"] = "m1" };
        var fp2 = new Dictionary<string, string> { ["machineId"] = "m2" };

        // 1. First checkout succeeds
        var chkRes1 = await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = key,
            FingerprintComponents = fp1
        });
        Assert.Equal(HttpStatusCode.OK, chkRes1.StatusCode);
        var lease1 = await chkRes1.Content.ReadFromJsonAsync<CheckoutResponseDto>();
        Assert.NotNull(lease1);
        Assert.NotEmpty(lease1.Token);

        // 2. Second checkout fails (pool exhausted)
        var chkRes2 = await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = key,
            FingerprintComponents = fp2
        });
        Assert.Equal(HttpStatusCode.Conflict, chkRes2.StatusCode);

        // 3. Renew lease 1
        var renewRes = await _client.PostAsJsonAsync($"/v1/leases/{lease1.LeaseId}/renew", new RenewRequestDto
        {
            ClientSeq = 0,
            FingerprintComponents = fp1
        });
        Assert.Equal(HttpStatusCode.OK, renewRes.StatusCode);
        var renewDto = await renewRes.Content.ReadFromJsonAsync<RenewResponseDto>();
        Assert.NotNull(renewDto);
        Assert.Equal(1, renewDto.LeaseSeq);

        // 4. Release lease 1
        var relRes = await _client.DeleteAsync($"/v1/leases/{lease1.LeaseId}");
        Assert.Equal(HttpStatusCode.OK, relRes.StatusCode);

        // 5. Now second checkout succeeds
        var chkRes3 = await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = key,
            FingerprintComponents = fp2
        });
        Assert.Equal(HttpStatusCode.OK, chkRes3.StatusCode);
        var lease2 = await chkRes3.Content.ReadFromJsonAsync<CheckoutResponseDto>();
        Assert.NotNull(lease2);
    }

    [Fact]
    public async Task NodeLock_Activation_And_Deactivation_Works()
    {
        var (_, key) = await CreateTestLicenseAsync(seats: 1);

        var fp = new Dictionary<string, string> { ["disk"] = "disk_abc_123" };

        // 1. Activate
        var actRes = await _client.PostAsJsonAsync("/v1/activations", new ActivationRequestDto
        {
            LicenseKey = key,
            FingerprintComponents = fp,
            MachineId = "host-01"
        });
        Assert.Equal(HttpStatusCode.OK, actRes.StatusCode);
        var act = await actRes.Content.ReadFromJsonAsync<ActivationResponseDto>();
        Assert.NotNull(act);
        Assert.Equal("active", act.State);

        // 2. Second machine fails because max seats = 1
        var actRes2 = await _client.PostAsJsonAsync("/v1/activations", new ActivationRequestDto
        {
            LicenseKey = key,
            FingerprintComponents = new Dictionary<string, string> { ["disk"] = "disk_other" },
            MachineId = "host-02"
        });
        Assert.Equal(HttpStatusCode.Conflict, actRes2.StatusCode);

        // 3. Deactivate first machine
        var deactRes = await _client.DeleteAsync($"/v1/activations/{act.ActivationId}");
        Assert.Equal(HttpStatusCode.OK, deactRes.StatusCode);
    }

    [Fact]
    public async Task LicenseFile_And_Jwks_Endpoints_Return_Valid_Documents()
    {
        var (_, key) = await CreateTestLicenseAsync(seats: 2);

        // File export
        var fileRes = await _client.GetAsync($"/v1/licenses/{key}/file");
        Assert.Equal(HttpStatusCode.OK, fileRes.StatusCode);
        var pem = await fileRes.Content.ReadAsStringAsync();
        Assert.Contains("-----BEGIN SYMBOLON LICENSE-----", pem, StringComparison.Ordinal);

        // JWKS
        var jwksRes = await _client.GetAsync("/v1/.well-known/symbolon-keys");
        Assert.Equal(HttpStatusCode.OK, jwksRes.StatusCode);
        var jwks = await jwksRes.Content.ReadFromJsonAsync<JsonWebKeySetDto>();
        Assert.NotNull(jwks);
        Assert.NotEmpty(jwks.Keys);
    }
}
