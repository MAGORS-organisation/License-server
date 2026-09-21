using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Symbolon.Crypto;
using Symbolon.Format;
using Symbolon.Protocol;
using Xunit;

namespace Symbolon.Relay.Tests;

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
}
