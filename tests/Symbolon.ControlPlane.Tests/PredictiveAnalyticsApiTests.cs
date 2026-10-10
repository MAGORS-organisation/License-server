using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Symbolon.Domain.Analytics;
using Symbolon.Domain.Security;
using Xunit;

namespace Symbolon.ControlPlane.Tests;

public sealed class PredictiveAnalyticsApiTests : IClassFixture<ControlPlaneFactory>
{
    private readonly HttpClient _client;

    public PredictiveAnalyticsApiTests(ControlPlaneFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task GetForecast_Endpoint_Returns_Valid_ForecastResult()
    {
        var response = await _client.GetAsync("/admin/v1/analytics/forecast?capacity=60&horizonHours=12");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ConcurrencyForecastResult>();
        result.Should().NotBeNull();
        result!.TotalCapacity.Should().Be(60);
        result.ForecastPoints.Should().HaveCount(12);
        result.RecommendedCapacityBuffer.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task GetAnomalies_Endpoint_Returns_Recent_Anomalies_List()
    {
        var response = await _client.GetAsync("/admin/v1/analytics/anomalies?limit=20");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var list = await response.Content.ReadFromJsonAsync<List<FraudRecord>>();
        list.Should().NotBeNull();
    }
}
