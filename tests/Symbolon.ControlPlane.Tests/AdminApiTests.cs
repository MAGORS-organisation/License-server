using System.Net;
using System.Net.Http.Json;
using Symbolon.ControlPlane.Models;
using Xunit;

namespace Symbolon.ControlPlane.Tests;

public sealed class AdminApiTests : IClassFixture<ControlPlaneFactory>
{
    private readonly HttpClient _client;

    public AdminApiTests(ControlPlaneFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Health_Endpoint_Returns_Healthy()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("healthy", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Admin_Can_Create_Tenant_Product_Policy_And_Issue_License()
    {
        // 1. Create Tenant
        var tenantRes = await _client.PostAsJsonAsync("/admin/v1/tenants", new CreateTenantDto("acme", "Acme Inc."));
        Assert.Equal(HttpStatusCode.Created, tenantRes.StatusCode);
        var tenant = await tenantRes.Content.ReadFromJsonAsync<TenantDto>();
        Assert.NotNull(tenant);

        // 2. Create Product
        var prodReq = new HttpRequestMessage(HttpMethod.Post, "/admin/v1/products")
        {
            Content = JsonContent.Create(new CreateProductDto("cad-pro", "CAD Pro Suite", ["windows", "linux"]))
        };
        prodReq.Headers.Add("X-Tenant-Id", tenant.Id);
        var prodRes = await _client.SendAsync(prodReq);
        Assert.Equal(HttpStatusCode.Created, prodRes.StatusCode);
        var product = await prodRes.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(product);

        // 3. Create Policy
        var polRes = await _client.PostAsJsonAsync("/admin/v1/policies", new CreatePolicyDto
        {
            ProductId = product.Id,
            Code = "floating-5-seats",
            Name = "Floating 5 seats",
            MaxSeats = 5,
            LicenseModel = "floating"
        });
        Assert.Equal(HttpStatusCode.Created, polRes.StatusCode);
        var policy = await polRes.Content.ReadFromJsonAsync<PolicyDto>();
        Assert.NotNull(policy);

        // 4. Issue License
        var licRes = await _client.PostAsJsonAsync("/admin/v1/licenses", new CreateLicenseDto
        {
            PolicyId = policy.Id,
            CustomerRef = "CUST-99",
            MaxSeats = 5
        });
        Assert.Equal(HttpStatusCode.Created, licRes.StatusCode);
        var license = await licRes.Content.ReadFromJsonAsync<LicenseResponseDto>();
        Assert.NotNull(license);
        Assert.NotNull(license.LicenseKey);
        Assert.StartsWith("SYM-", license.LicenseKey, StringComparison.Ordinal);
        Assert.Equal("active", license.State);
        Assert.Equal(5, license.MaxSeats);

        // 5. Concurrency report
        var reportRes = await _client.GetAsync("/admin/v1/reports/concurrency");
        Assert.Equal(HttpStatusCode.OK, reportRes.StatusCode);
        var report = await reportRes.Content.ReadFromJsonAsync<ConcurrencyReportDto>();
        Assert.NotNull(report);
        Assert.True(report.TotalSeats >= 5);

        // 6. Revoke License
        var revokeRes = await _client.PostAsJsonAsync($"/admin/v1/licenses/{license.Id}/revoke", new RevokeLicenseDto("Payment defaulted"));
        Assert.Equal(HttpStatusCode.OK, revokeRes.StatusCode);

        // Check revoked state
        var getLicRes = await _client.GetAsync($"/admin/v1/licenses/{license.Id}");
        var revokedLic = await getLicRes.Content.ReadFromJsonAsync<LicenseResponseDto>();
        Assert.NotNull(revokedLic);
        Assert.Equal("revoked", revokedLic.State);
    }
}
