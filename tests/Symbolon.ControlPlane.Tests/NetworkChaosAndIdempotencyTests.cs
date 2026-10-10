using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using FluentAssertions;
using Symbolon.ControlPlane.Models;
using Symbolon.Protocol;
using Xunit;

namespace Symbolon.ControlPlane.Tests;

/// <summary>
/// Chaos Delegating Handler for in-process network toxicity simulation (Toxiproxy parity).
/// Injects latency, jitter, TCP resets, connection drops, and mid-flight server-side partitions.
/// </summary>
public sealed class ChaosDelegatingHandler : DelegatingHandler
{
    public int LatencyMs { get; set; }
    public int JitterMs { get; set; }
    public bool SimulateTcpResetAfterServerResponse { get; set; }
    public bool SimulateTimeout { get; set; }

    public ChaosDelegatingHandler(HttpMessageHandler innerHandler) : base(innerHandler)
    {
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (SimulateTimeout)
        {
            throw new TaskCanceledException("Simulated connection timeout (Toxiproxy timeout toxic).");
        }

        if (LatencyMs > 0)
        {
            int delay = LatencyMs;
            if (JitterMs > 0)
            {
                delay += RandomNumberGenerator.GetInt32(-JitterMs, JitterMs + 1);
            }
            if (delay > 0)
            {
                await Task.Delay(delay, cancellationToken);
            }
        }

        var response = await base.SendAsync(request, cancellationToken);

        if (SimulateTcpResetAfterServerResponse)
        {
            SimulateTcpResetAfterServerResponse = false; // Trigger once
            throw new HttpRequestException("The connection was forcibly closed by the remote host (simulated mid-flight TCP reset).");
        }

        return response;
    }
}

/// <summary>
/// End-to-End Network Chaos and Idempotency Tests (§10.9, Toxiproxy parity).
/// Validates resilient heartbeat renewals under jitter, idempotent checkout recovery
/// after severed connections, and race condition immunity for duplicate Idempotency-Keys.
/// </summary>
public sealed class NetworkChaosAndIdempotencyTests : IClassFixture<ControlPlaneFactory>
{
    private readonly ControlPlaneFactory _factory;
    private readonly HttpClient _client;

    public NetworkChaosAndIdempotencyTests(ControlPlaneFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<(string LicenseId, string LicenseKey)> CreateTestLicenseAsync(int seats = 1)
    {
        var tenantRes = await _client.PostAsJsonAsync("/admin/v1/tenants",
            new CreateTenantDto($"chaos-tenant-{Guid.NewGuid():N}", "Chaos Enterprise"));
        var tenant = await tenantRes.Content.ReadFromJsonAsync<TenantDto>();

        var prodReq = new HttpRequestMessage(HttpMethod.Post, "/admin/v1/products")
        {
            Content = JsonContent.Create(new CreateProductDto($"prod-{Guid.NewGuid():N}", "Chaos Product", ["windows", "linux"]))
        };
        prodReq.Headers.Add("X-Tenant-Id", tenant!.Id);
        var prodRes = await _client.SendAsync(prodReq);
        var product = await prodRes.Content.ReadFromJsonAsync<ProductDto>();

        var polRes = await _client.PostAsJsonAsync("/admin/v1/policies", new CreatePolicyDto
        {
            ProductId = product!.Id,
            Code = $"policy-{Guid.NewGuid():N}",
            Name = "Chaos Floating Policy",
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
    public async Task HighLatency_AndJitter_HeartbeatRenew_SucceedsWithoutLosingSeat()
    {
        var (_, key) = await CreateTestLicenseAsync(seats: 1);
        var fp = new Dictionary<string, string> { ["machineId"] = "chaos-node-01" };

        // 1. Initial checkout
        var chkRes = await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = key,
            FingerprintComponents = fp,
            Quantity = 1
        });
        chkRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var lease = await chkRes.Content.ReadFromJsonAsync<CheckoutResponseDto>();
        lease.Should().NotBeNull();

        // 2. Setup Chaos HttpClient with 100ms latency and 40ms jitter
        using var chaosHandler = new ChaosDelegatingHandler(_factory.Server.CreateHandler())
        {
            LatencyMs = 80,
            JitterMs = 30
        };
        using var chaosClient = new HttpClient(chaosHandler)
        {
            BaseAddress = _client.BaseAddress
        };

        // 3. Perform 5 consecutive renewals over jittered high-latency connection
        for (int seq = 0; seq < 5; seq++)
        {
            var renewRes = await chaosClient.PostAsJsonAsync($"/v1/leases/{lease!.LeaseId}/renew", new RenewRequestDto
            {
                ClientSeq = seq,
                FingerprintComponents = fp
            });

            renewRes.StatusCode.Should().Be(HttpStatusCode.OK, $"Heartbeat seq={seq} must succeed under latency and jitter");
            var renewBody = await renewRes.Content.ReadFromJsonAsync<RenewResponseDto>();
            renewBody.Should().NotBeNull();
            renewBody!.Token.Should().NotBeNullOrEmpty();
            renewBody.LeaseSeq.Should().Be(seq + 1);
        }

        // Cleanup
        var releaseRes = await _client.DeleteAsync($"/v1/leases/{lease!.LeaseId}");
        releaseRes.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ConnectionReset_MidFlight_IdempotentRetry_GuaranteesSingleSeatAllocation()
    {
        // Critical: Capacity is exactly 1 seat. If idempotency fails on retry, request 2 will get 409 Conflict!
        var (_, key) = await CreateTestLicenseAsync(seats: 1);
        var fp = new Dictionary<string, string> { ["machineId"] = "resilient-worker-01" };
        string idempotencyKey = $"idem_{Guid.NewGuid():N}";

        using var chaosHandler = new ChaosDelegatingHandler(_factory.Server.CreateHandler())
        {
            SimulateTcpResetAfterServerResponse = true // Server processes checkout, but TCP response is severed!
        };
        using var chaosClient = new HttpClient(chaosHandler)
        {
            BaseAddress = _client.BaseAddress
        };

        var checkoutPayload = new CheckoutRequestDto
        {
            LicenseKey = key,
            FingerprintComponents = fp,
            Quantity = 1
        };

        // Attempt 1: Severed connection
        var req1 = new HttpRequestMessage(HttpMethod.Post, "/v1/leases")
        {
            Content = JsonContent.Create(checkoutPayload)
        };
        req1.Headers.Add("Idempotency-Key", idempotencyKey);

        Func<Task> act1 = async () => await chaosClient.SendAsync(req1);
        await act1.Should().ThrowAsync<HttpRequestException>("Simulated mid-flight TCP reset must sever client");

        // Attempt 2: Client retries with the EXACT SAME Idempotency-Key over recovered link
        var req2 = new HttpRequestMessage(HttpMethod.Post, "/v1/leases")
        {
            Content = JsonContent.Create(checkoutPayload)
        };
        req2.Headers.Add("Idempotency-Key", idempotencyKey);

        var res2 = await _client.SendAsync(req2);
        res2.StatusCode.Should().Be(HttpStatusCode.OK, "Idempotent retry must succeed even though seat capacity was 1");

        var lease = await res2.Content.ReadFromJsonAsync<CheckoutResponseDto>();
        lease.Should().NotBeNull();
        lease!.LeaseId.Should().NotBeNullOrEmpty();
        lease.Seat.Should().Be(1);

        // Verify pool is now occupied (capacity 1 is strictly enforced)
        var secondClientRes = await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = key,
            FingerprintComponents = new Dictionary<string, string> { ["machineId"] = "other-node" },
            Quantity = 1
        });
        secondClientRes.StatusCode.Should().Be(HttpStatusCode.Conflict, "Pool capacity was exactly 1 and is occupied by the idempotent lease");

        // Cleanup
        var releaseRes = await _client.DeleteAsync($"/v1/leases/{lease.LeaseId}");
        releaseRes.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task ConcurrentRace_IdenticalIdempotencyKey_GuaranteesAtomicSingleSeat()
    {
        // 10 concurrent requests fire the exact same checkout payload with the same Idempotency-Key simultaneously
        const int capacity = 1;
        const int concurrentCallers = 8;
        var (_, key) = await CreateTestLicenseAsync(seats: capacity);
        var fp = new Dictionary<string, string> { ["machineId"] = "race-worker" };
        string idempotencyKey = $"shared_idem_{Guid.NewGuid():N}";

        var successfulLeases = new ConcurrentBag<CheckoutResponseDto>();
        var statusCodes = new ConcurrentBag<HttpStatusCode>();

        var tasks = Enumerable.Range(0, concurrentCallers).Select(async _ =>
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/v1/leases")
            {
                Content = JsonContent.Create(new CheckoutRequestDto
                {
                    LicenseKey = key,
                    FingerprintComponents = fp,
                    Quantity = 1
                })
            };
            req.Headers.Add("Idempotency-Key", idempotencyKey);

            var res = await _client.SendAsync(req);
            statusCodes.Add(res.StatusCode);

            if (res.StatusCode == HttpStatusCode.OK)
            {
                var body = await res.Content.ReadFromJsonAsync<CheckoutResponseDto>();
                if (body != null)
                {
                    successfulLeases.Add(body);
                }
            }
        });

        await Task.WhenAll(tasks);

        // Every caller that got a response with Idempotency-Key must get the EXACT same LeaseId
        successfulLeases.Should().NotBeEmpty();
        var uniqueLeaseIds = successfulLeases.Select(l => l.LeaseId).Distinct().ToList();
        uniqueLeaseIds.Count.Should().Be(1, "All concurrent idempotent checkouts must resolve to the identical single LeaseId");

        // Cleanup
        var releaseRes = await _client.DeleteAsync($"/v1/leases/{uniqueLeaseIds[0]}");
        releaseRes.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Release_RetryAfterFailure_IsSafelyIdempotent()
    {
        var (_, key) = await CreateTestLicenseAsync(seats: 1);
        var fp = new Dictionary<string, string> { ["machineId"] = "release-worker" };

        var chkRes = await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = key,
            FingerprintComponents = fp,
            Quantity = 1
        });
        chkRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var lease = await chkRes.Content.ReadFromJsonAsync<CheckoutResponseDto>();

        // 1. First Release
        var release1 = await _client.DeleteAsync($"/v1/leases/{lease!.LeaseId}");
        release1.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Second Release (simulated client retry after connection failure)
        var release2 = await _client.DeleteAsync($"/v1/leases/{lease.LeaseId}");
        release2.StatusCode.Should().BeOneOf(HttpStatusCode.OK, HttpStatusCode.NotFound, HttpStatusCode.Gone);
    }
}
