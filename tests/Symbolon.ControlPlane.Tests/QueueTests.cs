using System.Net;
using System.Net.Http.Json;
using Symbolon.ControlPlane.Models;
using Symbolon.Protocol;
using Xunit;

namespace Symbolon.ControlPlane.Tests;

public sealed class QueueTests : IClassFixture<ControlPlaneFactory>
{
    private readonly HttpClient _client;

    public QueueTests(ControlPlaneFactory factory)
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
    public async Task Checkout_WhenPoolExhaustedAndAllowQueue_Returns202WithTicketAndHeaders()
    {
        var (licenseId, key) = await CreateTestLicenseAsync(seats: 1);

        var fp1 = new Dictionary<string, string> { ["machineId"] = "m1" };
        var fp2 = new Dictionary<string, string> { ["machineId"] = "m2" };

        // 1. First client takes the only seat
        var chkRes1 = await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = key,
            FingerprintComponents = fp1
        });
        Assert.Equal(HttpStatusCode.OK, chkRes1.StatusCode);

        // 2. Second client requests with AllowQueue: true
        var chkRes2 = await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = key,
            FingerprintComponents = fp2,
            AllowQueue = true
        });

        Assert.Equal(HttpStatusCode.Accepted, chkRes2.StatusCode);
        Assert.True(chkRes2.Headers.Contains("Retry-After"));

        var queued = await chkRes2.Content.ReadFromJsonAsync(SymbolonProtocolJsonContext.Default.QueuedResponseDto);
        Assert.NotNull(queued);
        Assert.Equal("queued", queued.Status);
        Assert.NotEmpty(queued.Ticket);
        Assert.Equal(1, queued.Position);
        Assert.True(queued.RetryAfterSeconds > 0);

        // 3. Query queue status
        var statusRes = await _client.GetAsync($"/v1/queue/{queued.Ticket}");
        Assert.Equal(HttpStatusCode.OK, statusRes.StatusCode);
        Assert.True(statusRes.Headers.Contains("Retry-After"));

        var status = await statusRes.Content.ReadFromJsonAsync(SymbolonProtocolJsonContext.Default.QueueStatusResponseDto);
        Assert.NotNull(status);
        Assert.Equal(queued.Ticket, status.Ticket);
        Assert.Equal("waiting", status.Status);
        Assert.Equal(1, status.Position);
    }

    [Fact]
    public async Task Queue_HigherPriorityTicket_JumpsAheadInLine()
    {
        var (licenseId, key) = await CreateTestLicenseAsync(seats: 1);

        // Fill seat
        var res1 = await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = key,
            FingerprintComponents = new Dictionary<string, string> { ["node"] = "fill" }
        });
        Assert.Equal(HttpStatusCode.OK, res1.StatusCode);

        // Enqueue normal priority (Priority: 0)
        var resNorm = await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = key,
            FingerprintComponents = new Dictionary<string, string> { ["node"] = "norm" },
            AllowQueue = true,
            Priority = 0
        });
        Assert.Equal(HttpStatusCode.Accepted, resNorm.StatusCode);
        var normTicket = (await resNorm.Content.ReadFromJsonAsync(SymbolonProtocolJsonContext.Default.QueuedResponseDto))!.Ticket;

        // Enqueue VIP priority (Priority: 10)
        var resVip = await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = key,
            FingerprintComponents = new Dictionary<string, string> { ["node"] = "vip" },
            AllowQueue = true,
            Priority = 10
        });
        Assert.Equal(HttpStatusCode.Accepted, resVip.StatusCode);
        var vipTicket = (await resVip.Content.ReadFromJsonAsync(SymbolonProtocolJsonContext.Default.QueuedResponseDto))!.Ticket;

        // Check positions: VIP must be #1, Normal must be #2
        var vipStatus = await (await _client.GetAsync($"/v1/queue/{vipTicket}")).Content.ReadFromJsonAsync(SymbolonProtocolJsonContext.Default.QueueStatusResponseDto);
        var normStatus = await (await _client.GetAsync($"/v1/queue/{normTicket}")).Content.ReadFromJsonAsync(SymbolonProtocolJsonContext.Default.QueueStatusResponseDto);

        Assert.NotNull(vipStatus);
        Assert.NotNull(normStatus);
        Assert.Equal(1, vipStatus.Position);
        Assert.Equal(2, normStatus.Position);
    }

    [Fact]
    public async Task Queue_AdminPromotion_TransitionsToReadyWithToken()
    {
        var (licenseId, key) = await CreateTestLicenseAsync(seats: 1);

        // Occupy seat
        var chkRes = await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = key,
            FingerprintComponents = new Dictionary<string, string> { ["hw"] = "first" }
        });
        var lease1 = (await chkRes.Content.ReadFromJsonAsync<CheckoutResponseDto>())!;

        // Enqueue second
        var qRes = await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = key,
            FingerprintComponents = new Dictionary<string, string> { ["hw"] = "second" },
            AllowQueue = true
        });
        var ticket = (await qRes.Content.ReadFromJsonAsync(SymbolonProtocolJsonContext.Default.QueuedResponseDto))!.Ticket;

        // Release first lease - triggers automatic seat promotion!
        var relRes = await _client.DeleteAsync($"/v1/leases/{lease1.LeaseId}");
        Assert.Equal(HttpStatusCode.OK, relRes.StatusCode);

        // Check ticket status is now ready with lease details
        var statusRes = await _client.GetAsync($"/v1/queue/{ticket}");
        var status = await statusRes.Content.ReadFromJsonAsync(SymbolonProtocolJsonContext.Default.QueueStatusResponseDto);

        Assert.NotNull(status);
        Assert.Equal("ready", status.Status);
        Assert.False(string.IsNullOrWhiteSpace(status.LeaseId));
        Assert.False(string.IsNullOrWhiteSpace(status.Token));
        Assert.NotNull(status.ExpiresAt);
    }

    [Fact]
    public async Task Queue_AdminPromotion_WhenCapacityExhausted_Returns409Conflict()
    {
        var (licenseId, key) = await CreateTestLicenseAsync(seats: 1);

        // Occupy seat
        await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = key,
            FingerprintComponents = new Dictionary<string, string> { ["hw"] = "first" }
        });

        // Enqueue second
        var qRes = await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = key,
            FingerprintComponents = new Dictionary<string, string> { ["hw"] = "second" },
            AllowQueue = true
        });
        var ticket = (await qRes.Content.ReadFromJsonAsync(SymbolonProtocolJsonContext.Default.QueuedResponseDto))!.Ticket;

        // Try promoting while seat is still occupied -> must fail with Conflict (409)
        var promoRes = await _client.PostAsync($"/admin/v1/queue/{ticket}/promote", null);
        Assert.Equal(HttpStatusCode.Conflict, promoRes.StatusCode);
    }

    [Fact]
    public async Task Queue_Cancel_Via_Public_Endpoint_RemovesFromWaitlist()
    {
        var (licenseId, key) = await CreateTestLicenseAsync(seats: 1);

        // Occupy
        await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = key,
            FingerprintComponents = new Dictionary<string, string> { ["hw"] = "occ" }
        });

        // Enqueue
        var qRes = await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = key,
            FingerprintComponents = new Dictionary<string, string> { ["hw"] = "to_cancel" },
            AllowQueue = true
        });
        var ticket = (await qRes.Content.ReadFromJsonAsync(SymbolonProtocolJsonContext.Default.QueuedResponseDto))!.Ticket;

        // Cancel
        var delRes = await _client.DeleteAsync($"/v1/queue/{ticket}");
        Assert.True(delRes.StatusCode is HttpStatusCode.OK or HttpStatusCode.NoContent);

        // Verify status returns 410 Gone for cancelled ticket
        var statusRes = await _client.GetAsync($"/v1/queue/{ticket}");
        Assert.Equal(HttpStatusCode.Gone, statusRes.StatusCode);
    }

    [Fact]
    public async Task Queue_Admin_List_Returns_Enqueued_Tickets()
    {
        var (licenseId, key) = await CreateTestLicenseAsync(seats: 1);

        // Occupy
        await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = key,
            FingerprintComponents = new Dictionary<string, string> { ["hw"] = "occ_list" }
        });

        // Enqueue
        var qRes = await _client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = key,
            FingerprintComponents = new Dictionary<string, string> { ["hw"] = "enq_list" },
            AllowQueue = true,
            UserId = "user-alice"
        });
        var ticket = (await qRes.Content.ReadFromJsonAsync(SymbolonProtocolJsonContext.Default.QueuedResponseDto))!.Ticket;

        // Admin list
        var listRes = await _client.GetAsync($"/admin/v1/queue?licenseId={licenseId}");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);

        var tickets = await listRes.Content.ReadFromJsonAsync(SymbolonProtocolJsonContext.Default.ListQueueTicketItemDto);
        Assert.NotNull(tickets);
        Assert.Contains(tickets, t => t.Ticket == ticket && t.UserId == "user-alice");
    }
}
