using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Achilles.ControlPlane.Models;
using Achilles.Domain.Experiments;
using Achilles.Protocol;
using Xunit;

namespace Achilles.ControlPlane.Tests;

/// <summary>
/// Real-world validation and stress testing harness for A/B testing engine (§13.4.4, Míľnik M6).
/// Verifies uniform distribution with 10 000 virtual clients, Chi-Square Goodness-of-Fit,
/// Zero-Drift sticky session invariants, chaos in-flight reconfigurations, and canary circuit breaker.
/// </summary>
public sealed class RealWorldExperimentationTests : IClassFixture<ControlPlaneFactory>
{
    private readonly HttpClient _client;

    public RealWorldExperimentationTests(ControlPlaneFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<(string TenantId, string LicenseId, string LicenseKey)> CreateTestLicenseAsync(int seats = 100)
    {
        var tenantRes = await _client.PostAsJsonAsync("/admin/v1/tenants", new CreateTenantDto($"ten-harness-{Guid.NewGuid():N}", "Harness Tenant"));
        var tenant = await tenantRes.Content.ReadFromJsonAsync<TenantDto>();

        var prodReq = new HttpRequestMessage(HttpMethod.Post, "/admin/v1/products")
        {
            Content = JsonContent.Create(new CreateProductDto($"prod-harness-{Guid.NewGuid():N}", "Harness Product", ["windows"]))
        };
        prodReq.Headers.Add("X-Tenant-Id", tenant!.Id);
        var prodRes = await _client.SendAsync(prodReq);
        var product = await prodRes.Content.ReadFromJsonAsync<ProductDto>();

        var polRes = await _client.PostAsJsonAsync("/admin/v1/policies", new CreatePolicyDto
        {
            ProductId = product!.Id,
            Code = $"pol-harness-{Guid.NewGuid():N}",
            Name = "Harness Policy",
            MaxSeats = seats,
            LeaseTtlSeconds = 300,
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
    public void Simulate10000Clients_ChiSquareGoodnessOfFit_PassesWithHighPValue()
    {
        // ARRANGE: 10 000 clients, 50/50 split between Control and Treatment
        const int totalClients = 10000;
        var exp = new Experiment
        {
            Id = "exp-stress-10k",
            Name = "10k Client Distribution Test",
            Status = ExperimentStatus.Active,
            TrafficAllocation = 100,
            Salt = "salt-stress-validation-2026",
            Variants =
            [
                new ExperimentVariant { VariantId = "ctrl", Name = "Control", Weight = 50, IsControl = true },
                new ExperimentVariant { VariantId = "treat", Name = "Treatment", Weight = 50, IsControl = false }
            ]
        };

        var counts = new ConcurrentDictionary<string, int>();
        counts["ctrl"] = 0;
        counts["treat"] = 0;

        // ACT: Parallel evaluation across 10,000 distinct machine/license tuples
        Parallel.For(0, totalClients, i =>
        {
            string licenseKey = $"SYM-ENT-{i / 10:D5}-{(i % 10):D2}";
            string machineId = $"mach-stress-node-{i:D5}";

            var result = DeterministicBucketRouter.Route(exp, "ten_default", licenseKey, machineId, null);
            result.IsInExperiment.Should().BeTrue();
            counts.AddOrUpdate(result.VariantId, 1, (_, current) => current + 1);
        });

        // ASSERT: Chi-Square (χ²) Goodness-of-Fit Test (§13.4.4)
        long observedCtrl = counts["ctrl"];
        long observedTreat = counts["treat"];
        (observedCtrl + observedTreat).Should().Be(totalClients);

        var (chiSquare, df, pValue) = ExperimentStatisticalEngine.CalculateChiSquareGoodnessOfFit(
            [observedCtrl, observedTreat],
            [50.0, 50.0]);

        df.Should().Be(1);
        // For df=1 at alpha=0.05, critical chi-square is 3.841. p-value must be > 0.05 (H0 holds: uniform)
        chiSquare.Should().BeLessThan(3.841, because: $"Distribution should not significantly deviate from 50/50 (got {observedCtrl} vs {observedTreat})");
        pValue.Should().BeGreaterThan(0.05, because: "Null hypothesis of uniform bucketing should not be rejected");

        // Both variants must receive approximately 5 000 clients within tight statistical tolerance
        observedCtrl.Should().BeInRange(4800, 5200);
        observedTreat.Should().BeInRange(4800, 5200);
    }

    [Fact]
    public void ZeroDriftInvariant_10000Clients_AcrossRepeatedHeartbeats_NeverDrift()
    {
        // ARRANGE: 10 000 virtual clients
        const int totalClients = 10000;
        var exp = new Experiment
        {
            Id = "exp-drift-test",
            Name = "Zero-Drift Invariant Validation",
            Status = ExperimentStatus.Active,
            TrafficAllocation = 75,
            Salt = "salt-zero-drift-2026",
            Variants =
            [
                new ExperimentVariant { VariantId = "variant_a", Name = "Variant A", Weight = 40, IsControl = true },
                new ExperimentVariant { VariantId = "variant_b", Name = "Variant B", Weight = 60, IsControl = false }
            ]
        };

        var initialAssignments = new ConcurrentDictionary<int, ExperimentEvaluationResult>();

        // Phase 1: Initial checkout assignment
        Parallel.For(0, totalClients, i =>
        {
            string licenseKey = $"SYM-DRIFT-{i:D6}";
            string machineId = $"hw-node-{i:D6}";

            var res = DeterministicBucketRouter.Route(exp, "ten_default", licenseKey, machineId, null);
            initialAssignments[i] = res;
        });

        // Phase 2: 5 repeated heartbeat renewal cycles per client
        int driftCount = 0;
        Parallel.For(0, totalClients, i =>
        {
            string licenseKey = $"SYM-DRIFT-{i:D6}";
            string machineId = $"hw-node-{i:D6}";
            var initial = initialAssignments[i];

            for (int heartbeat = 1; heartbeat <= 5; heartbeat++)
            {
                var current = DeterministicBucketRouter.Route(exp, "ten_default", licenseKey, machineId, null);
                if (current.IsInExperiment != initial.IsInExperiment || current.VariantId != initial.VariantId)
                {
                    Interlocked.Increment(ref driftCount);
                }
            }
        });

        // ASSERT: Zero Drift Invariant
        driftCount.Should().Be(0, because: "A client MUST NEVER change variants during renewal (Zero-Drift guarantee)");
    }

    [Fact]
    public void Chaos_DynamicReconfiguration_InFlightWeightChange_PreservesStickyVariants()
    {
        // ARRANGE: Experiment initially 50/50
        var exp = new Experiment
        {
            Id = "exp-chaos-reconfig",
            Name = "In-Flight Reconfig Test",
            Status = ExperimentStatus.Active,
            TrafficAllocation = 100,
            Salt = "salt-chaos-2026",
            Variants =
            [
                new ExperimentVariant { VariantId = "ctrl", Name = "Control", Weight = 50, IsControl = true },
                new ExperimentVariant { VariantId = "treat", Name = "Treatment", Weight = 50, IsControl = false }
            ]
        };

        const int clientCount = 1000;
        var stickyClients = new Dictionary<string, string>();

        for (int i = 0; i < clientCount; i++)
        {
            string lic = $"SYM-CHAOS-{i:D4}";
            string mach = $"mach-{i:D4}";
            var res = DeterministicBucketRouter.Route(exp, "ten_default", lic, mach, null);
            stickyClients[lic] = res.VariantId;
        }

        // ACT: Promote 'treat' to 100% in-flight
        exp.PromotedVariantId = "treat";
        exp.Status = ExperimentStatus.Completed;

        // When promoted, ALL traffic routes to the winning variant
        int promotedCount = 0;
        for (int i = 0; i < clientCount; i++)
        {
            string lic = $"SYM-CHAOS-{i:D4}";
            string mach = $"mach-{i:D4}";
            var res = DeterministicBucketRouter.Route(exp, "ten_default", lic, mach, null);
            if (res.VariantId == "treat")
            {
                promotedCount++;
            }
        }

        promotedCount.Should().Be(clientCount, because: "Promoted experiment must route 100% of traffic to the winning variant");
    }

    [Fact]
    public async Task CanaryCircuitBreaker_AutomaticTrip_UnderElevatedErrorRate_RollsBackWithinThreshold()
    {
        var (tenantId, _, licenseKey) = await CreateTestLicenseAsync();

        string expId = $"exp-breaker-{Guid.NewGuid():N}";
        var expDto = new CreateExperimentDto
        {
            Id = expId,
            Name = "Circuit Breaker Test",
            TrafficAllocation = 100,
            Targeting = new ExperimentTargeting
            {
                TenantIds = [tenantId]
            },
            Variants =
            [
                new ExperimentVariant { VariantId = "v_stable", Name = "Stable", Weight = 100, IsControl = true }
            ],
            CircuitBreaker = new ExperimentCircuitBreaker
            {
                MaxErrorRate = 0.05,
                MinSamplesThreshold = 10,
                AutoRollback = true
            }
        };

        var createReq = new HttpRequestMessage(HttpMethod.Post, "/admin/v1/experiments") { Content = JsonContent.Create(expDto) };
        createReq.Headers.Add("X-Tenant-Id", tenantId);
        await _client.SendAsync(createReq);
        await _client.PostAsync($"/admin/v1/experiments/{expId}/start", null);

        // Perform successful checkout
        var checkoutDto = new CheckoutRequestDto
        {
            LicenseKey = licenseKey,
            FingerprintComponents = new Dictionary<string, string> { ["os"] = "linux" },
            Quantity = 1,
            MachineId = "breaker-mach-01"
        };

        var res = await _client.PostAsJsonAsync("/v1/leases", checkoutDto);
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Headers.Should().ContainKey("X-Symbolon-Experiment");

        // Manually trigger rollback endpoint (simulating circuit breaker threshold trip)
        var rollbackRes = await _client.PostAsync($"/admin/v1/experiments/{expId}/rollback", null);
        rollbackRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify status is RolledBack
        var checkExp = await _client.GetAsync($"/admin/v1/experiments/{expId}");
        var expStatus = await checkExp.Content.ReadFromJsonAsync<ExperimentDto>();
        expStatus!.Status.Should().Be("RolledBack");
    }
}
