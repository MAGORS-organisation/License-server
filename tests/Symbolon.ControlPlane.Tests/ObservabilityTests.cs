using System.Net;
using Xunit;

namespace Symbolon.ControlPlane.Tests;

public sealed class ObservabilityTests : IClassFixture<ControlPlaneFactory>
{
    private readonly HttpClient _client;

    public ObservabilityTests(ControlPlaneFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Metrics_Endpoint_Returns_Prometheus_Format()
    {
        var response = await _client.GetAsync("/metrics");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("symbolon_seats_active", content, StringComparison.Ordinal);
        Assert.Contains("symbolon_checkout_denied_total", content, StringComparison.Ordinal);
        Assert.Contains("symbolon_lease_renewed_total", content, StringComparison.Ordinal);
        Assert.Contains("symbolon_lease_released_total", content, StringComparison.Ordinal);
        Assert.Contains("symbolon_clock_skew_detected_total", content, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Health_Live_Returns_Alive()
    {
        var response = await _client.GetAsync("/health/live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("alive", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Health_Ready_Returns_Ready_When_Database_Available()
    {
        var response = await _client.GetAsync("/health/ready");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("ready", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("connected", content, StringComparison.OrdinalIgnoreCase);
    }
}
