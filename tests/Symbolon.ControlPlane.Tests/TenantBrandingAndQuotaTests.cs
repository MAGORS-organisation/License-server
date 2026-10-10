using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Symbolon.ControlPlane.Models;
using Symbolon.Protocol;
using Xunit;

namespace Symbolon.ControlPlane.Tests;

public sealed class TenantBrandingAndQuotaTests : IClassFixture<ControlPlaneFactory>
{
    private readonly HttpClient _client;

    public TenantBrandingAndQuotaTests(ControlPlaneFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetTenantBranding_ReturnsDefault_WhenUnconfigured()
    {
        string uniqueSlug = $"fresh-tenant-{Guid.NewGuid():N}";
        var createRes = await _client.PostAsJsonAsync("/admin/v1/tenants", new CreateTenantDto(uniqueSlug, "Fresh Org"));
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var tenant = await createRes.Content.ReadFromJsonAsync<TenantDto>();

        var response = await _client.GetAsync($"/admin/v1/tenants/{tenant!.Id}/branding");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var branding = await response.Content.ReadFromJsonAsync<TenantBrandingDto>();
        branding.Should().NotBeNull();
        branding!.TenantId.Should().Be(tenant.Id);
        branding.CompanyName.Should().Be("Fresh Org");
        branding.PrimaryColorHex.Should().Be("#1E40AF");
        branding.AccentColorHex.Should().Be("#3B82F6");
    }

    [Fact]
    public async Task UpdateTenantBranding_Persists_And_PublicPortal_Exposes_It()
    {
        var updatePayload = new UpdateTenantBrandingDto(
            CompanyName: "Acme Cybernetics Corp",
            LogoUrl: "https://acme.internal/assets/logo.svg",
            PrimaryColorHex: "#0D9488",
            AccentColorHex: "#14B8A6",
            PortalTitle: "Acme Enterprise Self-Service Portal",
            CustomCss: ".navbar { background-color: #0D9488; }");

        var updateRes = await _client.PostAsJsonAsync("/admin/v1/tenants/ten_default/branding", updatePayload);
        updateRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var updated = await updateRes.Content.ReadFromJsonAsync<TenantBrandingDto>();
        updated.Should().NotBeNull();
        updated!.CompanyName.Should().Be("Acme Cybernetics Corp");
        updated.PrimaryColorHex.Should().Be("#0D9488");
        updated.PortalTitle.Should().Be("Acme Enterprise Self-Service Portal");

        // Verify public portal branding without admin credentials
        var publicRes = await _client.GetAsync("/v1/portal/branding");
        publicRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var publicBranding = await publicRes.Content.ReadFromJsonAsync<TenantBrandingDto>();
        publicBranding.Should().NotBeNull();
        publicBranding!.CompanyName.Should().Be("Acme Cybernetics Corp");
        publicBranding.PrimaryColorHex.Should().Be("#0D9488");
    }

    [Fact]
    public async Task DepartmentQuotas_Set_List_And_Enforce_StrictQuota_OnCheckout()
    {
        // 1. Set quota of 1 seat for "DesignDept"
        var quotaDto = new SetDepartmentQuotaDto("DesignDept", 1, EnforceStrictQuota: true);
        var setRes = await _client.PostAsJsonAsync("/admin/v1/tenants/ten_default/departments", quotaDto);
        setRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var setQuota = await setRes.Content.ReadFromJsonAsync<TenantDepartmentQuotaDto>();
        setQuota.Should().NotBeNull();
        setQuota!.DepartmentName.Should().Be("DesignDept");
        setQuota.AllocatedSeats.Should().Be(1);

        // 2. Fetch department summary
        var summaryRes = await _client.GetAsync("/admin/v1/tenants/ten_default/departments");
        summaryRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var summary = await summaryRes.Content.ReadFromJsonAsync<DepartmentUsageSummaryDto>();
        summary.Should().NotBeNull();
        summary!.Departments.Should().Contain(d => d.DepartmentName == "DesignDept" && d.AllocatedSeats == 1);

        // 3. Issue a test license to checkout against
        var productRes = await _client.PostAsJsonAsync("/admin/v1/products", new CreateProductDto("DEPT-CAD", "Dept CAD Suite", ["windows"]));
        productRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var product = await productRes.Content.ReadFromJsonAsync<ProductDto>();

        var policyRes = await _client.PostAsJsonAsync("/admin/v1/policies", new CreatePolicyDto
        {
            ProductId = product!.Id,
            Code = "DEPT-POL-01",
            Name = "Dept Policy",
            MaxSeats = 10,
            LeaseTtlSeconds = 300
        });
        policyRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var policy = await policyRes.Content.ReadFromJsonAsync<PolicyDto>();

        var licRes = await _client.PostAsJsonAsync("/admin/v1/licenses", new CreateLicenseDto
        {
            PolicyId = policy!.Id,
            CustomerRef = "acme-cust-dept",
            MaxSeats = 10
        });
        licRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var lic = await licRes.Content.ReadFromJsonAsync<LicenseResponseDto>();

        // 4. First checkout for DesignDept succeeds
        var checkout1 = await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = lic!.LicenseKey!,
            FingerprintComponents = new Dictionary<string, string> { ["host"] = "dept-pc-1" },
            Department = "DesignDept"
        });
        checkout1.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. Second checkout for DesignDept exceeds allocated quota of 1 -> 409 Conflict
        var checkout2 = await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = lic.LicenseKey!,
            FingerprintComponents = new Dictionary<string, string> { ["host"] = "dept-pc-2" },
            Department = "DesignDept"
        });
        checkout2.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // 6. Delete department quota
        var delRes = await _client.DeleteAsync("/admin/v1/tenants/ten_default/departments/DesignDept");
        delRes.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
