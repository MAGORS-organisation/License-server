using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Symbolon.Data.Entities;
using Symbolon.Data.Stores;
using Symbolon.Domain.Webhooks;
using Xunit;

namespace Symbolon.Data.Tests;

public sealed class EfWebhookStoreTests : IDisposable
{
    private static readonly string[] SampleEvents = ["lease.denied", "license.expired"];
    private static readonly string[] WildcardEvent = ["*"];

    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<SymbolonDbContext> _options;

    public EfWebhookStoreTests()
    {
        _connection = new SqliteConnection("Filename=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<SymbolonDbContext>()
            .UseSqlite(_connection)
            .Options;
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private async Task<string> SeedTenantAsync()
    {
        await using var db = new SymbolonDbContext(_options);
        await db.Database.EnsureCreatedAsync();

        string tenantId = $"ten_{Guid.NewGuid():N}";
        db.Tenants.Add(new Tenant
        {
            Id = tenantId,
            Slug = $"tenant-{Guid.NewGuid():N}",
            Name = "Test Tenant",
            CreatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        return tenantId;
    }

    [Fact]
    public async Task CreateAndListSubscriptions_WorksCorrectly()
    {
        var tenantId = await SeedTenantAsync();
        await using var db = new SymbolonDbContext(_options);
        var store = new EfWebhookStore(db, TimeProvider.System);

        var created = await store.CreateSubscriptionAsync(
            tenantId: tenantId,
            name: "Slack Alerts",
            url: "https://hooks.slack.com/services/test",
            secret: "secret123",
            format: "slack",
            events: SampleEvents);

        created.Name.Should().Be("Slack Alerts");
        created.Format.Should().Be("SLACK");

        var list = await store.GetSubscriptionsAsync(tenantId);
        list.Should().HaveCount(1);
        list[0].Url.Should().Be("https://hooks.slack.com/services/test");
        list[0].Events.Should().Contain(SampleEvents);
    }

    [Fact]
    public async Task LogDeliveryAndFilterByStatus_WorksCorrectly()
    {
        var tenantId = await SeedTenantAsync();
        await using var db = new SymbolonDbContext(_options);
        var store = new EfWebhookStore(db, TimeProvider.System);

        var sub = await store.CreateSubscriptionAsync(
            tenantId: tenantId,
            name: "Audit Webhook",
            url: "https://example.com/wh",
            secret: "sec",
            format: "json",
            events: WildcardEvent);

        var delivery1 = new WebhookDeliveryModel(
            Id: $"del_{Guid.NewGuid():N}",
            SubscriptionId: sub.Id,
            EventType: "license.created",
            PayloadJson: "{}",
            Status: "delivered",
            StatusCode: 200,
            DurationMs: 42,
            Attempts: 1,
            DeliveredAt: DateTimeOffset.UtcNow,
            LastError: null,
            CreatedAt: DateTimeOffset.UtcNow);

        var delivery2 = new WebhookDeliveryModel(
            Id: $"del_{Guid.NewGuid():N}",
            SubscriptionId: sub.Id,
            EventType: "lease.denied",
            PayloadJson: "{}",
            Status: "dead_letter",
            StatusCode: 500,
            DurationMs: 150,
            Attempts: 3,
            DeliveredAt: null,
            LastError: "Connection refused",
            CreatedAt: DateTimeOffset.UtcNow);

        await store.LogDeliveryAsync(delivery1);
        await store.LogDeliveryAsync(delivery2);

        var all = await store.GetDeliveriesAsync(subscriptionId: sub.Id);
        all.Should().HaveCount(2);

        var deliveredOnly = await store.GetDeliveriesAsync(subscriptionId: sub.Id, status: "delivered");
        deliveredOnly.Should().HaveCount(1);
        deliveredOnly[0].StatusCode.Should().Be(200);

        var dlqOnly = await store.GetDeliveriesAsync(subscriptionId: sub.Id, status: "dead_letter");
        dlqOnly.Should().HaveCount(1);
        dlqOnly[0].Status.Should().Be("dead_letter");
        dlqOnly[0].DurationMs.Should().Be(150);
    }

    [Fact]
    public async Task RecordDeliveryAttempt_UpdatesSubscriptionHealth()
    {
        var tenantId = await SeedTenantAsync();
        await using var db = new SymbolonDbContext(_options);
        var store = new EfWebhookStore(db, TimeProvider.System);

        var sub = await store.CreateSubscriptionAsync(
            tenantId: tenantId,
            name: "Health Webhook",
            url: "https://example.com/api",
            secret: "sec",
            format: "json",
            events: WildcardEvent);

        // Record a failure
        await store.UpdateSubscriptionFailureAsync(sub.Id, success: false);

        var loaded = await store.GetSubscriptionByIdAsync(sub.Id);
        loaded.Should().NotBeNull();
        loaded!.FailureCount.Should().Be(1);

        // Record success
        await store.UpdateSubscriptionFailureAsync(sub.Id, success: true);
        var loadedAfterSuccess = await store.GetSubscriptionByIdAsync(sub.Id);
        loadedAfterSuccess!.FailureCount.Should().Be(0);
        loadedAfterSuccess.LastDeliveredAt.Should().NotBeNull();
    }
}
