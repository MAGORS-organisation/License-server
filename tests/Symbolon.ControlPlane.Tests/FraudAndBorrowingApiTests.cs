using System.Net;
using System.Net.Http.Json;
using Symbolon.ControlPlane.Models;
using Symbolon.Protocol;
using Xunit;

namespace Symbolon.ControlPlane.Tests;

public sealed class FraudAndBorrowingApiTests : IClassFixture<ControlPlaneFactory>
{
    private readonly HttpClient _client;

    public FraudAndBorrowingApiTests(ControlPlaneFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetFraudRadar_ReturnsOk()
    {
        var res = await _client.GetAsync("/admin/v1/fraud/radar");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var radar = await res.Content.ReadFromJsonAsync<List<FraudRadarAdminDto>>();
        Assert.NotNull(radar);
    }

    [Fact]
    public async Task GetBorrowedSeats_ReturnsOk()
    {
        var res = await _client.GetAsync("/admin/v1/leases/borrowed");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);

        var seats = await res.Content.ReadFromJsonAsync<List<BorrowedSeatAdminDto>>();
        Assert.NotNull(seats);
    }

    [Fact]
    public async Task Checkout_WithImpossibleTravel_RecordsAnomalyInFraudRadar()
    {
        // 1. Create Tenant, Product, Policy, License
        string slug = $"tenant-fraud-{Guid.NewGuid():N}";
        var tenantRes = await _client.PostAsJsonAsync("/admin/v1/tenants", new CreateTenantDto(slug, "Fraud Detection Corp"));
        Assert.Equal(HttpStatusCode.Created, tenantRes.StatusCode);
        var tenant = await tenantRes.Content.ReadFromJsonAsync<TenantDto>();
        Assert.NotNull(tenant);

        var prodReq = new HttpRequestMessage(HttpMethod.Post, "/admin/v1/products")
        {
            Content = JsonContent.Create(new CreateProductDto($"app-fraud-{Guid.NewGuid():N}"[..12], "App", ["windows"]))
        };
        prodReq.Headers.Add("X-Tenant-Id", tenant.Id);
        var prodRes = await _client.SendAsync(prodReq);
        Assert.Equal(HttpStatusCode.Created, prodRes.StatusCode);
        var product = await prodRes.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(product);

        var polRes = await _client.PostAsJsonAsync("/admin/v1/policies", new CreatePolicyDto
        {
            ProductId = product.Id,
            Code = "floating-fraud-10",
            Name = "Floating 10 seats",
            MaxSeats = 10,
            LicenseModel = "floating"
        });
        Assert.Equal(HttpStatusCode.Created, polRes.StatusCode);
        var policy = await polRes.Content.ReadFromJsonAsync<PolicyDto>();
        Assert.NotNull(policy);

        var licRes = await _client.PostAsJsonAsync("/admin/v1/licenses", new CreateLicenseDto
        {
            PolicyId = policy.Id,
            CustomerRef = "CUST-FRAUD-01",
            MaxSeats = 10
        });
        Assert.Equal(HttpStatusCode.Created, licRes.StatusCode);
        var license = await licRes.Content.ReadFromJsonAsync<LicenseResponseDto>();
        Assert.NotNull(license);
        Assert.NotNull(license.LicenseKey);

        // 2. First checkout from Bratislava
        var req1 = new HttpRequestMessage(HttpMethod.Post, "/v1/leases")
        {
            Content = JsonContent.Create(new CheckoutRequestDto
            {
                LicenseKey = license.LicenseKey!,
                FingerprintComponents = new Dictionary<string, string> { ["host"] = "host1" },
                MachineId = "host-bts",
                UserId = "traveler_joe",
                Quantity = 1
            })
        };
        req1.Headers.Add("X-Forwarded-For", "85.237.100.1"); // Bratislava
        var res1 = await _client.SendAsync(req1);
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);

        // 3. Second checkout 1 second later from Tokyo (Impossible Travel!)
        var req2 = new HttpRequestMessage(HttpMethod.Post, "/v1/leases")
        {
            Content = JsonContent.Create(new CheckoutRequestDto
            {
                LicenseKey = license.LicenseKey!,
                FingerprintComponents = new Dictionary<string, string> { ["host"] = "host2" },
                MachineId = "host-tyo",
                UserId = "traveler_joe",
                Quantity = 1
            })
        };
        req2.Headers.Add("X-Forwarded-For", "133.242.1.1"); // Tokyo
        var res2 = await _client.SendAsync(req2);
        Assert.Equal(HttpStatusCode.OK, res2.StatusCode);

        // 4. Verify fraud radar captured the anomaly
        var radarRes = await _client.GetAsync("/admin/v1/fraud/radar");
        Assert.Equal(HttpStatusCode.OK, radarRes.StatusCode);
        var radar = await radarRes.Content.ReadFromJsonAsync<List<FraudRadarAdminDto>>();
        Assert.NotNull(radar);
        Assert.Contains(radar, r => r.RiskType == "impossible_travel" && r.UserId == "traveler_joe");
    }

    [Fact]
    public async Task BorrowSeat_ThenAdminReturnsSeat_Succeeds()
    {
        // 1. Create Tenant, Product, Policy, License
        string slug = $"tenant-borrow-{Guid.NewGuid():N}";
        var tenantRes = await _client.PostAsJsonAsync("/admin/v1/tenants", new CreateTenantDto(slug, "Borrow Corp"));
        Assert.Equal(HttpStatusCode.Created, tenantRes.StatusCode);
        var tenant = await tenantRes.Content.ReadFromJsonAsync<TenantDto>();
        Assert.NotNull(tenant);

        var prodReq = new HttpRequestMessage(HttpMethod.Post, "/admin/v1/products")
        {
            Content = JsonContent.Create(new CreateProductDto($"app-borrow-{Guid.NewGuid():N}"[..12], "Borrow App", ["windows"]))
        };
        prodReq.Headers.Add("X-Tenant-Id", tenant.Id);
        var prodRes = await _client.SendAsync(prodReq);
        Assert.Equal(HttpStatusCode.Created, prodRes.StatusCode);
        var product = await prodRes.Content.ReadFromJsonAsync<ProductDto>();
        Assert.NotNull(product);

        var polRes = await _client.PostAsJsonAsync("/admin/v1/policies", new CreatePolicyDto
        {
            ProductId = product.Id,
            Code = "floating-borrow-5",
            Name = "Floating 5 seats",
            MaxSeats = 5,
            LicenseModel = "floating"
        });
        Assert.Equal(HttpStatusCode.Created, polRes.StatusCode);
        var policy = await polRes.Content.ReadFromJsonAsync<PolicyDto>();
        Assert.NotNull(policy);

        var licRes = await _client.PostAsJsonAsync("/admin/v1/licenses", new CreateLicenseDto
        {
            PolicyId = policy.Id,
            CustomerRef = "CUST-BORROW-01",
            MaxSeats = 5
        });
        Assert.Equal(HttpStatusCode.Created, licRes.StatusCode);
        var license = await licRes.Content.ReadFromJsonAsync<LicenseResponseDto>();
        Assert.NotNull(license);
        Assert.NotNull(license.LicenseKey);

        // 2. Acquire a seat
        var checkoutRes = await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = license.LicenseKey!,
            FingerprintComponents = new Dictionary<string, string> { ["host"] = "cad-laptop-01" },
            Quantity = 1
        });
        Assert.Equal(HttpStatusCode.OK, checkoutRes.StatusCode);
        var checkoutDto = await checkoutRes.Content.ReadFromJsonAsync<CheckoutResponseDto>();
        Assert.NotNull(checkoutDto);

        // 3. Borrow the seat for 10 days
        var borrowRes = await _client.PostAsJsonAsync($"/v1/leases/{checkoutDto.LeaseId}/borrow", new BorrowRequestDto(10));
        Assert.Equal(HttpStatusCode.OK, borrowRes.StatusCode);
        var borrowDto = await borrowRes.Content.ReadFromJsonAsync<BorrowResponseDto>();
        Assert.NotNull(borrowDto);
        Assert.Equal(checkoutDto.LeaseId, borrowDto.LeaseId);

        // 4. Verify admin lists the borrowed seat
        var listRes = await _client.GetAsync("/admin/v1/leases/borrowed");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var borrowedList = await listRes.Content.ReadFromJsonAsync<List<BorrowedSeatAdminDto>>();
        Assert.NotNull(borrowedList);
        Assert.Contains(borrowedList, s => s.LeaseId == checkoutDto.LeaseId);

        // 5. Admin forcefully returns the seat
        var returnRes = await _client.PostAsync($"/admin/v1/leases/{checkoutDto.LeaseId}/return", null);
        Assert.Equal(HttpStatusCode.OK, returnRes.StatusCode);

        // 6. Verify it is no longer listed in borrowed seats
        var listRes2 = await _client.GetAsync("/admin/v1/leases/borrowed");
        var borrowedList2 = await listRes2.Content.ReadFromJsonAsync<List<BorrowedSeatAdminDto>>();
        Assert.NotNull(borrowedList2);
        Assert.DoesNotContain(borrowedList2, s => s.LeaseId == checkoutDto.LeaseId);
    }
}
