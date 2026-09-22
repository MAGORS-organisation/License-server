using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Symbolon.ControlPlane.Models;
using Symbolon.Domain.Webhooks;
using Xunit;

namespace Symbolon.ControlPlane.Tests;

public sealed class WebhookReplayAndLifecycleApiTests : IClassFixture<ControlPlaneFactory>
{
    private static readonly string[] TestEvents = ["license.created", "lease.denied", "license.expiring_soon"];

    private readonly HttpClient _client;

    public WebhookReplayAndLifecycleApiTests(ControlPlaneFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task WebhookReplay_ReturnsNotFound_WhenDeliveryDoesNotExist()
    {
        string fakeId = $"del_{Guid.NewGuid():N}";
        var response = await _client.PostAsync($"/admin/v1/webhooks/deliveries/{fakeId}/replay", null);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreateSubscription_WithEnterpriseFormatAndName_Succeeds()
    {
        var dto = new CreateWebhookDto(
            Url: "https://hooks.slack.com/services/T00/B00/XXXX",
            Events: TestEvents,
            Secret: "secret-abc",
            Name: "Slack DevOps Alerts",
            Format: "slack");

        var response = await _client.PostAsJsonAsync("/admin/v1/webhooks", dto);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<WebhookSubscriptionDto>();
        created.Should().NotBeNull();
        created!.Name.Should().Be("Slack DevOps Alerts");
        created.Format.Should().Be("SLACK");
    }

    [Fact]
    public async Task LifecycleEvaluationEndpoints_WorkCorrectly()
    {
        // 1. Query expiring licenses endpoint
        var expiringRes = await _client.GetAsync("/admin/v1/lifecycle/expiring?thresholdDays=30");
        expiringRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var expiringReport = await expiringRes.Content.ReadFromJsonAsync<JsonElement>();
        expiringReport.ValueKind.Should().Be(JsonValueKind.Object);
        expiringReport.TryGetProperty("items", out var items).Should().BeTrue();
        items.ValueKind.Should().Be(JsonValueKind.Array);

        // 2. Trigger lifecycle evaluation
        var evalRes = await _client.PostAsync("/admin/v1/lifecycle/evaluate?expiringSoonDays=14&softGraceDays=7", null);
        evalRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var report = await evalRes.Content.ReadFromJsonAsync<JsonElement>();
        report.TryGetProperty("evaluatedLicenses", out var evalCount).Should().BeTrue();
        evalCount.GetInt32().Should().BeGreaterThanOrEqualTo(0);
        report.TryGetProperty("eventsDispatched", out var dispatchedCount).Should().BeTrue();
        dispatchedCount.GetInt32().Should().BeGreaterThanOrEqualTo(0);
    }
}
