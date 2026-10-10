using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Achilles.ControlPlane.Models;
using Achilles.Protocol;
using Xunit;

namespace Achilles.ControlPlane.Tests;

/// <summary>
/// End-to-End High-Concurrency Load and Chaos Benchmarks (§10.9 & Smer 3).
/// Validates HTTP pipeline, EF Core database locking, race condition prevention,
/// and latency percentiles under concurrent burst load.
/// </summary>
public sealed class HighConcurrencyLoadTests : IClassFixture<ControlPlaneFactory>
{
    private readonly HttpClient _client;

    public HighConcurrencyLoadTests(ControlPlaneFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<(string LicenseId, string LicenseKey)> CreateTestLicenseAsync(int seats = 20)
    {
        var tenantRes = await _client.PostAsJsonAsync("/admin/v1/tenants",
            new CreateTenantDto($"load-tenant-{Guid.NewGuid():N}", "Load Test Enterprise"));
        var tenant = await tenantRes.Content.ReadFromJsonAsync<TenantDto>();

        var prodReq = new HttpRequestMessage(HttpMethod.Post, "/admin/v1/products")
        {
            Content = JsonContent.Create(new CreateProductDto($"prod-{Guid.NewGuid():N}", "Load App", ["windows", "linux"]))
        };
        prodReq.Headers.Add("X-Tenant-Id", tenant!.Id);
        var prodRes = await _client.SendAsync(prodReq);
        var product = await prodRes.Content.ReadFromJsonAsync<ProductDto>();

        var polRes = await _client.PostAsJsonAsync("/admin/v1/policies", new CreatePolicyDto
        {
            ProductId = product!.Id,
            Code = $"policy-{Guid.NewGuid():N}",
            Name = "Floating Load Policy",
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
    public async Task HighConcurrency_100ParallelCheckouts_ZeroOverAllocations_LatencyPercentilesWithinBounds()
    {
        const int totalCapacity = 20;
        const int concurrentClients = 100;

        var (_, licenseKey) = await CreateTestLicenseAsync(seats: totalCapacity);

        var successfulLeases = new ConcurrentBag<CheckoutResponseDto>();
        var deniedCount = 0;
        var latenciesMs = new ConcurrentBag<double>();

        using var barrier = new SemaphoreSlim(0, concurrentClients);

        var tasks = Enumerable.Range(0, concurrentClients).Select(async i =>
        {
            var req = new CheckoutRequestDto
            {
                LicenseKey = licenseKey,
                MachineId = $"load-worker-{i}",
                FingerprintComponents = new Dictionary<string, string> { ["worker_id"] = $"node_{i}" },
                Quantity = 1,
                AllowQueue = false
            };

            barrier.Release();
            await Task.Yield();

            var sw = Stopwatch.StartNew();
            var response = await _client.PostAsJsonAsync("/v1/leases", req);
            sw.Stop();
            latenciesMs.Add(sw.Elapsed.TotalMilliseconds);

            if (response.StatusCode == HttpStatusCode.OK)
            {
                var body = await response.Content.ReadFromJsonAsync<CheckoutResponseDto>();
                if (body != null)
                {
                    successfulLeases.Add(body);
                }
            }
            else if (response.StatusCode == HttpStatusCode.Conflict || response.StatusCode == HttpStatusCode.BadRequest)
            {
                Interlocked.Increment(ref deniedCount);
            }
        });

        await Task.WhenAll(tasks);

        // Invariant 1: Exactly totalCapacity seats must be granted
        successfulLeases.Count.Should().Be(totalCapacity,
            $"Concurrency Invariant: exactly {totalCapacity} seats should be successfully allocated out of {concurrentClients} requests");

        // Invariant 2: Exactly (concurrentClients - totalCapacity) requests must be rejected
        deniedCount.Should().Be(concurrentClients - totalCapacity,
            $"Rejection Invariant: remaining {concurrentClients - totalCapacity} clients must be rejected");

        // Invariant 3: Unique Lease IDs (no duplicate seats or tokens)
        var leaseIds = successfulLeases.Select(l => l.LeaseId).Distinct().ToList();
        leaseIds.Count.Should().Be(totalCapacity, "All issued leases must have distinct Lease IDs");

        // Latency assertions
        var sortedLatencies = latenciesMs.OrderBy(l => l).ToList();
        var p50 = sortedLatencies[(int)(concurrentClients * 0.50)];
        var p95 = sortedLatencies[(int)(concurrentClients * 0.95)];

        p50.Should().BeLessThan(1000.0, "p50 checkout latency must be responsive under concurrent burst");
        p95.Should().BeLessThan(2000.0, "p95 checkout latency must remain under 2 seconds under high contention");

        // Cleanup & Drain: release all allocated seats
        foreach (var lease in successfulLeases)
        {
            var releaseRes = await _client.DeleteAsync($"/v1/leases/{lease.LeaseId}");
            releaseRes.StatusCode.Should().Be(HttpStatusCode.OK);
        }

        // Verify pool immediately recovers: next checkout succeeds!
        var recoveryCheckout = await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = licenseKey,
            MachineId = "recovery-worker",
            FingerprintComponents = new Dictionary<string, string> { ["worker_id"] = "recovery" },
            Quantity = 1
        });
        recoveryCheckout.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task HighConcurrency_BurstHeartbeats_ParallelThroughput_ZeroErrors()
    {
        const int seatCount = 10;
        var (_, licenseKey) = await CreateTestLicenseAsync(seats: seatCount);

        // Initial checkouts
        var activeLeases = new List<(CheckoutResponseDto Lease, Dictionary<string, string> Fp)>();
        for (int i = 0; i < seatCount; i++)
        {
            var fp = new Dictionary<string, string> { ["worker"] = $"hb_{i}" };
            var res = await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
            {
                LicenseKey = licenseKey,
                MachineId = $"hb-worker-{i}",
                FingerprintComponents = fp,
                Quantity = 1
            });
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var body = await res.Content.ReadFromJsonAsync<CheckoutResponseDto>();
            activeLeases.Add((body!, fp));
        }

        // Parallel burst renewals (100 total heartbeats across 10 workers concurrently)
        const int renewalsPerWorker = 10;
        var successCount = 0;

        var tasks = activeLeases.Select(async item =>
        {
            for (int seq = 0; seq < renewalsPerWorker; seq++)
            {
                var renewRes = await _client.PostAsJsonAsync($"/v1/leases/{item.Lease.LeaseId}/renew", new RenewRequestDto
                {
                    ClientSeq = seq,
                    FingerprintComponents = item.Fp
                });

                if (renewRes.StatusCode == HttpStatusCode.OK)
                {
                    Interlocked.Increment(ref successCount);
                }
            }
        });

        await Task.WhenAll(tasks);

        successCount.Should().Be(seatCount * renewalsPerWorker,
            "All parallel heartbeats should succeed without deadlocks or concurrency errors");
    }
}
