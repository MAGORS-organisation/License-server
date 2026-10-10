using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Achilles.ControlPlane.Models;
using Achilles.Domain.Experiments;
using Achilles.Protocol;
using Xunit;

namespace Achilles.ControlPlane.Tests;

public sealed class ExperimentationTests : IClassFixture<ControlPlaneFactory>
{
    private readonly HttpClient _client;

    public ExperimentationTests(ControlPlaneFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<(string TenantId, string LicenseId, string LicenseKey)> CreateTestLicenseAsync(int seats = 5)
    {
        var tenantRes = await _client.PostAsJsonAsync("/admin/v1/tenants", new CreateTenantDto($"ten-exp-{Guid.NewGuid():N}", "Tenant for Experiments"));
        var tenant = await tenantRes.Content.ReadFromJsonAsync<TenantDto>();

        var prodReq = new HttpRequestMessage(HttpMethod.Post, "/admin/v1/products")
        {
            Content = JsonContent.Create(new CreateProductDto($"prod-exp-{Guid.NewGuid():N}", "Product", ["windows"]))
        };
        prodReq.Headers.Add("X-Tenant-Id", tenant!.Id);
        var prodRes = await _client.SendAsync(prodReq);
        var product = await prodRes.Content.ReadFromJsonAsync<ProductDto>();

        var polRes = await _client.PostAsJsonAsync("/admin/v1/policies", new CreatePolicyDto
        {
            ProductId = product!.Id,
            Code = $"policy-exp-{Guid.NewGuid():N}",
            Name = "Floating Exp Policy",
            MaxSeats = seats,
            LeaseTtlSeconds = 600,
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
    public async Task AdminApi_ExperimentLifecycle_WorksEndToEnd()
    {
        var (tenantId, _, _) = await CreateTestLicenseAsync();

        string expId = $"exp-{Guid.NewGuid():N}";
        var createDto = new CreateExperimentDto
        {
            Id = expId,
            Name = "TTL 300s vs 60s Test",
            Description = "Testing impact of short lease TTL on renewal traffic",
            TrafficAllocation = 100,
            Targeting = new ExperimentTargeting
            {
                TenantIds = [tenantId]
            },
            Variants =
            [
                new ExperimentVariant
                {
                    VariantId = "ctrl",
                    Name = "Control (600s)",
                    Weight = 50,
                    IsControl = true,
                    Overrides = new ExperimentOverrides { LeaseTtlSeconds = 600 }
                },
                new ExperimentVariant
                {
                    VariantId = "treat",
                    Name = "Treatment (60s)",
                    Weight = 50,
                    IsControl = false,
                    Overrides = new ExperimentOverrides { LeaseTtlSeconds = 60 }
                }
            ],
            CircuitBreaker = new ExperimentCircuitBreaker
            {
                MaxErrorRate = 0.05,
                MinSamplesThreshold = 20,
                AutoRollback = true
            }
        };

        // 1. Create Experiment (Draft)
        var createReq = new HttpRequestMessage(HttpMethod.Post, "/admin/v1/experiments")
        {
            Content = JsonContent.Create(createDto)
        };
        createReq.Headers.Add("X-Tenant-Id", tenantId);
        var createRes = await _client.SendAsync(createReq);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await createRes.Content.ReadFromJsonAsync<ExperimentDto>();
        created!.Id.Should().Be(expId);
        created.Status.Should().Be("Draft");

        // 2. Start Experiment (Active)
        var startRes = await _client.PostAsync($"/admin/v1/experiments/{expId}/start", null);
        startRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var started = await startRes.Content.ReadFromJsonAsync<ExperimentDto>();
        started!.Status.Should().Be("Active");
        started.StartedAt.Should().NotBeNull();

        // 3. Simulate Experiment
        var simRes = await _client.PostAsJsonAsync($"/admin/v1/experiments/{expId}/simulate", new SimulateExperimentDto(ClientCount: 500, TenantId: tenantId));
        simRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var sim = await simRes.Content.ReadFromJsonAsync<SimulateExperimentResultDto>();
        sim!.TotalSimulated.Should().Be(500);
        sim.TotalInExperiment.Should().Be(500);
        sim.VariantCounts.Should().ContainKey("ctrl");
        sim.VariantCounts.Should().ContainKey("treat");

        // 4. Pause Experiment
        var pauseRes = await _client.PostAsync($"/admin/v1/experiments/{expId}/pause", null);
        pauseRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var paused = await pauseRes.Content.ReadFromJsonAsync<ExperimentDto>();
        paused!.Status.Should().Be("Paused");

        // 5. Promote Variant (Completed)
        var promoteRes = await _client.PostAsync($"/admin/v1/experiments/{expId}/promote/treat", null);
        promoteRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var promoted = await promoteRes.Content.ReadFromJsonAsync<ExperimentDto>();
        promoted!.Status.Should().Be("Completed");
        promoted.PromotedVariantId.Should().Be("treat");

        // 6. Get Report
        var reportRes = await _client.GetAsync($"/admin/v1/experiments/{expId}/report");
        reportRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var report = await reportRes.Content.ReadFromJsonAsync<ExperimentStatisticalReport>();
        report.Should().NotBeNull();
        report!.ExperimentId.Should().Be(expId);

        // 7. Delete Experiment
        var delRes = await _client.DeleteAsync($"/admin/v1/experiments/{expId}");
        delRes.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task PublicCheckout_EmitsExperimentHeader_AndAppliesOverrides()
    {
        var (tenantId, _, licenseKey) = await CreateTestLicenseAsync();

        string expId = $"exp-live-{Guid.NewGuid():N}";
        var expDto = new CreateExperimentDto
        {
            Id = expId,
            Name = "Live Override Test",
            TrafficAllocation = 100,
            Targeting = new ExperimentTargeting
            {
                TenantIds = [tenantId]
            },
            Variants =
            [
                new ExperimentVariant
                {
                    VariantId = "v_short",
                    Name = "Short TTL 45s",
                    Weight = 100,
                    Overrides = new ExperimentOverrides { LeaseTtlSeconds = 45 }
                }
            ]
        };

        // Create and Start
        var createReq = new HttpRequestMessage(HttpMethod.Post, "/admin/v1/experiments") { Content = JsonContent.Create(expDto) };
        createReq.Headers.Add("X-Tenant-Id", tenantId);
        await _client.SendAsync(createReq);
        await _client.PostAsync($"/admin/v1/experiments/{expId}/start", null);

        // Checkout
        var checkoutDto = new CheckoutRequestDto
        {
            LicenseKey = licenseKey,
            FingerprintComponents = new Dictionary<string, string> { ["os"] = "windows" },
            Quantity = 1,
            MachineId = "mach-001"
        };

        var checkoutRes = await _client.PostAsJsonAsync("/v1/leases", checkoutDto);
        checkoutRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify X-Symbolon-Experiment response header
        checkoutRes.Headers.Should().ContainKey("X-Symbolon-Experiment");
        var expHeader = checkoutRes.Headers.GetValues("X-Symbolon-Experiment").FirstOrDefault();
        expHeader.Should().Be($"{expId}=v_short");

        // Verify TTL override: 45 seconds
        var body = await checkoutRes.Content.ReadFromJsonAsync<CheckoutResponseDto>();
        body!.ExpiresAt.Should().BeCloseTo(DateTimeOffset.UtcNow.AddSeconds(45), TimeSpan.FromSeconds(5));

        // Verify report metrics updated
        var reportRes = await _client.GetAsync($"/admin/v1/experiments/{expId}/report");
        var report = await reportRes.Content.ReadFromJsonAsync<ExperimentStatisticalReport>();
        report.Should().NotBeNull();
    }
}
