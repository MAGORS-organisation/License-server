using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Achilles.Crypto;
using Achilles.Format;
using Achilles.Protocol;
using Xunit;

namespace Achilles.Relay.Tests;

public sealed class RelayApiTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RelayApiTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsHealthy()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("healthy");
    }

    [Fact]
    public async Task HealthLiveEndpoint_ReturnsHealthy()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/live");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("healthy");
    }

    [Fact]
    public async Task HealthReadyEndpoint_ReturnsReady()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/ready");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("ready");
    }

    [Fact]
    public async Task HealthGrantEndpoint_ReturnsGrantStatus()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/health/grant");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        string content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("activeGrants");
    }

    [Fact]
    public async Task MetricsEndpoint_ReturnsPrometheusMetrics()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/metrics");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/plain");

        string content = await response.Content.ReadAsStringAsync();
        content.Should().Contain("symbolon_relay_seats_allocated");
        content.Should().Contain("symbolon_relay_seats_total");
        content.Should().Contain("symbolon_relay_active_grants");
    }

    [Fact]
    public async Task WellKnownKeysEndpoint_ReturnsJwks()
    {
        var client = _factory.CreateClient();
        var response = await client.GetAsync("/v1/.well-known/symbolon-keys");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var jwks = await response.Content.ReadFromJsonAsync<JsonWebKeySetDto>();
        jwks.Should().NotBeNull();
        jwks!.Keys.Should().NotBeEmpty();
        jwks.Keys[0].Kty.Should().Be("EC");
        jwks.Keys[0].Alg.Should().Be("ES256");
    }

    [Fact]
    public async Task Checkout_WithInvalidLicenseKey_ReturnsBadRequest()
    {
        var client = _factory.CreateClient();
        var dto = new CheckoutRequestDto
        {
            LicenseKey = "INVALID-KEY-NOT-CROCKFORD",
            FingerprintComponents = new Dictionary<string, string> { ["pc"] = "test-pc" }
        };

        var response = await client.PostAsJsonAsync("/v1/leases", dto);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task FullFloatingLifecycle_Checkout_Renew_Release_Succeeds()
    {
        var key = LicenseKey.Generate();
        var client = _factory.CreateClient();

        // Seed seat for this generated key
        using (var scope = _factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<SqliteSeatStore>();
            await store.SeedSeatsAsync(key.Canonical, seatCount: 5);
        }

        var components = new Dictionary<string, string>
        {
            ["machineId"] = "PC-WORKSTATION-42",
            ["cpu"] = "INTEL-I9"
        };

        // 1. Checkout
        var checkoutDto = new CheckoutRequestDto
        {
            LicenseKey = key.Canonical,
            FingerprintComponents = components,
            Quantity = 1,
            Features = ["core", "module.cad-export"]
        };

        var checkoutRes = await client.PostAsJsonAsync("/v1/leases", checkoutDto);
        checkoutRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var checkoutBody = await checkoutRes.Content.ReadFromJsonAsync<CheckoutResponseDto>();
        checkoutBody.Should().NotBeNull();
        checkoutBody!.LeaseId.Should().StartWith("lse_");
        checkoutBody.Token.Should().NotBeNullOrWhiteSpace();
        checkoutBody.Seat.Should().Be(0);

        string leaseId = checkoutBody.LeaseId;

        // 2. Renew
        var renewDto = new RenewRequestDto
        {
            ClientSeq = 0,
            FingerprintComponents = components
        };

        var renewRes = await client.PostAsJsonAsync($"/v1/leases/{leaseId}/renew", renewDto);
        renewRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var renewBody = await renewRes.Content.ReadFromJsonAsync<RenewResponseDto>();
        renewBody.Should().NotBeNull();
        renewBody!.LeaseSeq.Should().Be(1);

        // 3. Release
        var releaseRes = await client.DeleteAsync($"/v1/leases/{leaseId}");
        releaseRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var releaseBody = await releaseRes.Content.ReadFromJsonAsync<ReleaseResponseDto>();
        releaseBody.Should().NotBeNull();
        releaseBody!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Checkout_WhenPoolExhaustedAndAllowQueue_ReturnsAccepted_AndCanPollStatusAndCancel()
    {
        var client = _factory.CreateClient();
        var store = _factory.Services.GetRequiredService<SqliteSeatStore>();

        var key = LicenseKey.Generate();
        await store.SeedSeatsAsync(key.Canonical, seatCount: 1);

        var components1 = new Dictionary<string, string> { ["host"] = "host-1" };
        var components2 = new Dictionary<string, string> { ["host"] = "host-2" };

        // 1. Occupy the single seat
        var chk1 = await client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = key.Canonical,
            FingerprintComponents = components1
        });
        chk1.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Second checkout with AllowQueue
        var chk2 = await client.PostAsJsonAsync("/v1/leases", new CheckoutRequestDto
        {
            LicenseKey = key.Canonical,
            FingerprintComponents = components2,
            AllowQueue = true
        });
        chk2.StatusCode.Should().Be(HttpStatusCode.Accepted);
        chk2.Headers.Contains("Retry-After").Should().BeTrue();

        var queued = await chk2.Content.ReadFromJsonAsync(AchillesProtocolJsonContext.Default.QueuedResponseDto);
        queued.Should().NotBeNull();
        queued!.Ticket.Should().StartWith("q_");
        queued.Position.Should().Be(1);

        // 3. Poll queue status
        var statusRes = await client.GetAsync($"/v1/queue/{queued.Ticket}");
        statusRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var status = await statusRes.Content.ReadFromJsonAsync(AchillesProtocolJsonContext.Default.QueueStatusResponseDto);
        status.Should().NotBeNull();
        status!.Status.Should().Be("waiting");

        // 4. Cancel ticket
        var cancelRes = await client.DeleteAsync($"/v1/queue/{queued.Ticket}");
        cancelRes.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }
}
