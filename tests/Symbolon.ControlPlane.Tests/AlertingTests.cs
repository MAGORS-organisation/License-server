using Microsoft.Extensions.Logging.Abstractions;
using Symbolon.ControlPlane.Alerting;
using Symbolon.ControlPlane.Models;
using Symbolon.ControlPlane.Observability;
using Symbolon.ControlPlane.Webhooks;
using Symbolon.Domain;
using Xunit;

namespace Symbolon.ControlPlane.Tests;

public sealed class AlertingTests
{
    private sealed class FakeWebhookDispatcher : IWebhookDispatcher
    {
        public List<(string EventType, object Payload, string? TenantId)> Published { get; } = [];

        public Task PublishEventAsync(string eventType, object payload, string? tenantId = null, CancellationToken ct = default)
        {
            Published.Add((eventType, payload, tenantId));
            return Task.CompletedTask;
        }

        public Task<WebhookTestResultDto> TestPingAsync(string subscriptionId, CancellationToken ct = default)
        {
            return Task.FromResult(new WebhookTestResultDto(true, 200, "ok", 10, null));
        }

        public Task<WebhookDeliveryDto?> ReplayDeliveryAsync(string deliveryId, CancellationToken ct = default)
        {
            return Task.FromResult<WebhookDeliveryDto?>(null);
        }
    }

    [Fact]
    public async Task CapacityAlert_Triggers_When_Utilization_Exceeds_90Percent()
    {
        var fakeWebhooks = new FakeWebhookDispatcher();
        var fakeAudit = new InMemoryAuditLedger();
        using var metrics = new SymbolonMetrics();
        var alertService = new AlertService(fakeWebhooks, fakeAudit, metrics, TimeProvider.System, NullLogger<AlertService>.Instance);

        // 9 active out of 10 max = 90%
        await alertService.CheckCapacityThresholdAsync("lic_test_123", activeSeats: 9, maxSeats: 10, tenantId: "ten_test");

        Assert.Single(fakeWebhooks.Published);
        Assert.Equal("alert.capacity_exhausted", fakeWebhooks.Published[0].EventType);
        Assert.Equal("ten_test", fakeWebhooks.Published[0].TenantId);

        Assert.Single(fakeAudit.Events);
        Assert.Equal("alert.capacity_exhausted", fakeAudit.Events[0].Type);

        // Under 90% (e.g. 5 out of 10 = 50%) -> should NOT trigger
        var fakeWebhooksUnder = new FakeWebhookDispatcher();
        var alertServiceUnder = new AlertService(fakeWebhooksUnder, fakeAudit, metrics, TimeProvider.System, NullLogger<AlertService>.Instance);

        await alertServiceUnder.CheckCapacityThresholdAsync("lic_test_456", activeSeats: 5, maxSeats: 10, tenantId: "ten_test");
        Assert.Empty(fakeWebhooksUnder.Published);
    }

    [Fact]
    public async Task DenialSpikeAlert_Triggers_After_Five_Rapid_Denials()
    {
        var fakeWebhooks = new FakeWebhookDispatcher();
        var fakeAudit = new InMemoryAuditLedger();
        using var metrics = new SymbolonMetrics();
        var alertService = new AlertService(fakeWebhooks, fakeAudit, metrics, TimeProvider.System, NullLogger<AlertService>.Instance);

        // First 4 denials: should not trigger alert yet
        for (int i = 0; i < 4; i++)
        {
            await alertService.RecordDenialSpikeAsync("lic_spike_1", "ten_test", "seat-pool-exhausted");
        }

        Assert.Empty(fakeWebhooks.Published);

        // 5th denial within the minute window triggers the alert!
        await alertService.RecordDenialSpikeAsync("lic_spike_1", "ten_test", "seat-pool-exhausted");

        Assert.Single(fakeWebhooks.Published);
        Assert.Equal("alert.denial_spike", fakeWebhooks.Published[0].EventType);
        Assert.Equal("ten_test", fakeWebhooks.Published[0].TenantId);
    }
}
