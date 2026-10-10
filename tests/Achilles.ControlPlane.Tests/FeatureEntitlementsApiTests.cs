using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Achilles.ControlPlane.Models;
using Achilles.Domain.Entitlements;
using Achilles.Protocol;
using Xunit;

namespace Achilles.ControlPlane.Tests;

public sealed class FeatureEntitlementsApiTests : IClassFixture<ControlPlaneFactory>
{
    private readonly HttpClient _client;

    public FeatureEntitlementsApiTests(ControlPlaneFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<(string TenantId, string LicenseId, string LicenseKey)> CreateTestLicenseAsync(int seats = 2)
    {
        var tenantRes = await _client.PostAsJsonAsync("/admin/v1/tenants", new CreateTenantDto($"tenant-{Guid.NewGuid():N}", "Tenant for Features"));
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

        return (tenant.Id, license!.Id, license.LicenseKey!);
    }

    [Fact]
    public async Task AdminApi_FeaturesAndSuitesCrud_WorksCorrectly()
    {
        var (tenantId, licenseId, _) = await CreateTestLicenseAsync();

        // 1. Create Feature Definition
        var createFeatDto = new CreateFeatureDto(
            Code: "CAD_3D",
            Name: "3D CAD Modeling",
            Description: "Kernel modeling engine",
            IsFloating: true,
            DefaultMaxSeats: 5);

        var featRes = await _client.PostAsJsonAsync("/admin/v1/entitlements/features", createFeatDto);
        featRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var feat = await featRes.Content.ReadFromJsonAsync<FeatureDefinitionModel>();
        feat.Should().NotBeNull();
        feat!.Code.Should().Be("CAD_3D");

        // 2. List Feature Definitions
        var listRes = await _client.GetAsync("/admin/v1/entitlements/features");
        listRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var list = await listRes.Content.ReadFromJsonAsync<List<FeatureDefinitionModel>>();
        list.Should().Contain(f => f.Code == "CAD_3D");

        // 3. Create Package Suite
        var createSuiteDto = new CreatePackageSuiteDto(
            Code: "CAD_SUITE",
            Name: "CAD Full Suite",
            FeatureCodes: ["CAD_3D", "FEA_SOLVER"]);

        var suiteRes = await _client.PostAsJsonAsync("/admin/v1/entitlements/suites", createSuiteDto);
        suiteRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var suite = await suiteRes.Content.ReadFromJsonAsync<PackageSuiteModel>();
        suite.Should().NotBeNull();
        suite!.FeatureCodes.Should().Contain("CAD_3D");

        // 4. Set License Entitlement
        var setEntitlementDto = new SetLicenseEntitlementDto(
            FeatureCode: "CAD_3D",
            MaxSeats: 2,
            AllowedVersionRange: "2026.*",
            IsEnabled: true);

        var entRes = await _client.PostAsJsonAsync($"/admin/v1/entitlements/licenses/{licenseId}", setEntitlementDto);
        entRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. Get License Entitlements
        var getEntRes = await _client.GetAsync($"/admin/v1/entitlements/licenses/{licenseId}");
        getEntRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var entitlements = await getEntRes.Content.ReadFromJsonAsync<List<LicenseEntitlementModel>>();
        entitlements.Should().Contain(e => e.FeatureCode == "CAD_3D" && e.MaxSeats == 2);
    }

    [Fact]
    public async Task PublicApi_DynamicFeatureAcquireReleaseAndCapacityLimit_WorksEndToEnd()
    {
        var (tenantId, licenseId, licenseKey) = await CreateTestLicenseAsync(seats: 2);

        // Set entitlement for FEA_SOLVER with limit of 1 seat
        var setEntitlementDto = new SetLicenseEntitlementDto(
            FeatureCode: "FEA_SOLVER",
            MaxSeats: 1,
            AllowedVersionRange: "*",
            IsEnabled: true);
        var entRes = await _client.PostAsJsonAsync($"/admin/v1/entitlements/licenses/{licenseId}", setEntitlementDto);
        entRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Checkout 2 seats from the license
        var fp1 = new Dictionary<string, string> { ["machineId"] = "machine-1" };
        var chk1 = await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = licenseKey,
            FingerprintComponents = fp1
        });
        chk1.StatusCode.Should().Be(HttpStatusCode.OK);
        var lease1 = await chk1.Content.ReadFromJsonAsync<CheckoutResponseDto>();
        lease1.Should().NotBeNull();

        var fp2 = new Dictionary<string, string> { ["machineId"] = "machine-2" };
        var chk2 = await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = licenseKey,
            FingerprintComponents = fp2
        });
        chk2.StatusCode.Should().Be(HttpStatusCode.OK);
        var lease2 = await chk2.Content.ReadFromJsonAsync<CheckoutResponseDto>();
        lease2.Should().NotBeNull();

        // 1. Lease 1 acquires FEA_SOLVER -> Succeeded
        var acq1Res = await _client.PostAsJsonAsync($"/v1/leases/{lease1!.LeaseId}/features/acquire", new AcquireFeatureRequestDto
        {
            FeatureCode = "FEA_SOLVER",
            Version = "2026.1"
        });
        acq1Res.StatusCode.Should().Be(HttpStatusCode.OK);
        var acq1 = await acq1Res.Content.ReadFromJsonAsync<FeatureAcquisitionResponseDto>();
        acq1.Should().NotBeNull();
        acq1!.Success.Should().BeTrue();
        acq1.InUse.Should().Be(1);

        // 2. Query active features on Lease 1
        var listFeatRes = await _client.GetAsync($"/v1/leases/{lease1.LeaseId}/features");
        listFeatRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var activeFeats = await listFeatRes.Content.ReadFromJsonAsync<List<ActiveFeatureInfoDto>>();
        activeFeats.Should().Contain(f => f.FeatureCode == "FEA_SOLVER");

        // 3. Lease 2 attempts to acquire FEA_SOLVER -> Denied (capacity = 1 exceeded)
        var acq2Res = await _client.PostAsJsonAsync($"/v1/leases/{lease2!.LeaseId}/features/acquire", new AcquireFeatureRequestDto
        {
            FeatureCode = "FEA_SOLVER",
            Version = "2026.1"
        });
        acq2Res.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // 4. Lease 1 releases FEA_SOLVER
        var relRes = await _client.PostAsJsonAsync($"/v1/leases/{lease1.LeaseId}/features/release", new ReleaseFeatureRequestDto
        {
            FeatureCode = "FEA_SOLVER"
        });
        relRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var rel = await relRes.Content.ReadFromJsonAsync<ReleaseFeatureResponseDto>();
        rel.Should().NotBeNull();
        rel!.Success.Should().BeTrue();

        // 5. Now Lease 2 can acquire FEA_SOLVER
        var acq2Retry = await _client.PostAsJsonAsync($"/v1/leases/{lease2.LeaseId}/features/acquire", new AcquireFeatureRequestDto
        {
            FeatureCode = "FEA_SOLVER",
            Version = "2026.1"
        });
        acq2Retry.StatusCode.Should().Be(HttpStatusCode.OK);

        // 6. Release main lease 2 -> Releases all features
        var relLeaseRes = await _client.DeleteAsync($"/v1/leases/{lease2.LeaseId}");
        relLeaseRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var listAfterRelease = await _client.GetAsync($"/v1/leases/{lease2.LeaseId}/features");
        listAfterRelease.StatusCode.Should().Be(HttpStatusCode.OK);
        var activeAfter = await listAfterRelease.Content.ReadFromJsonAsync<List<ActiveFeatureInfoDto>>();
        activeAfter.Should().BeEmpty();
    }
}
