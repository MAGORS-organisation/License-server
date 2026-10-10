using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Achilles.ControlPlane.Models;
using Achilles.ControlPlane.Webhooks;
using Achilles.Data;
using Achilles.Data.Entities;
using Xunit;

namespace Achilles.ControlPlane.Tests;

public sealed class WebhookDlqAndRetryTests : IClassFixture<ControlPlaneFactory>
{
    private readonly ControlPlaneFactory _factory;
    private readonly HttpClient _client;

    public WebhookDlqAndRetryTests(ControlPlaneFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public void WebhookBackoffHelper_CalculatesExponentialBackoffWithJitter()
    {
        var delay1 = WebhookBackoffHelper.CalculateBackoff(1);
        var delay2 = WebhookBackoffHelper.CalculateBackoff(2);
        var delay3 = WebhookBackoffHelper.CalculateBackoff(3);

        delay1.TotalSeconds.Should().BeInRange(2.0, 3.1);
        delay2.TotalSeconds.Should().BeInRange(4.0, 5.1);
        delay3.TotalSeconds.Should().BeInRange(8.0, 9.1);
    }

    [Fact]
    public async Task DeadLetterQueue_Endpoints_ListAndPurgeSuccessfully()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AchillesDbContext>();

        string tenantId = $"tenant_dlq_{Guid.NewGuid():N}";
        db.Tenants.Add(new Tenant { Id = tenantId, Slug = tenantId, Name = "DLQ Tenant", CreatedAt = DateTimeOffset.UtcNow });

        string subId = $"sub_dlq_{Guid.NewGuid():N}";
        var sub = new WebhookSubscriptionEntity
        {
            Id = subId,
            TenantId = tenantId,
            Url = "https://example.com/webhook",
            Name = "DLQ Test Sub",
            Format = "json",
            EventsJson = "[\"*\"]",
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow
        };
        db.WebhookSubscriptions.Add(sub);

        string delId1 = $"del_dlq_1_{Guid.NewGuid():N}";
        string delId2 = $"del_dlq_2_{Guid.NewGuid():N}";

        db.WebhookDeliveries.AddRange(
            new WebhookDeliveryEntity
            {
                Id = delId1,
                SubscriptionId = subId,
                EventType = "license.created",
                PayloadJson = "{}",
                Status = "dead_letter",
                Attempts = 5,
                LastError = "Connection refused after 5 attempts",
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-10)
            },
            new WebhookDeliveryEntity
            {
                Id = delId2,
                SubscriptionId = subId,
                EventType = "lease.denied",
                PayloadJson = "{}",
                Status = "dead_letter",
                Attempts = 5,
                LastError = "HTTP 500 Internal Server Error",
                CreatedAt = DateTimeOffset.UtcNow.AddMinutes(-5)
            }
        );
        await db.SaveChangesAsync();

        // 1. GET /admin/v1/webhooks/dlq
        var getRes = await _client.GetAsync(new Uri("/admin/v1/webhooks/dlq?limit=100", UriKind.Relative));
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var dlqList = await getRes.Content.ReadFromJsonAsync<List<WebhookDeliveryDto>>();
        dlqList.Should().NotBeNull();
        dlqList!.Any(d => d.Id == delId1).Should().BeTrue();
        dlqList.Any(d => d.Id == delId2).Should().BeTrue();

        // 2. DELETE /admin/v1/webhooks/dlq/{delId1}
        var delRes = await _client.DeleteAsync(new Uri($"/admin/v1/webhooks/dlq/{delId1}", UriKind.Relative));
        delRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify delId1 purged
        using (var verifyScope = _factory.Services.CreateScope())
        {
            var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AchillesDbContext>();
            var exists1 = await verifyDb.WebhookDeliveries.AnyAsync(d => d.Id == delId1);
            exists1.Should().BeFalse();
        }

        // 3. POST /admin/v1/webhooks/dlq/purge-all
        var purgeRes = await _client.PostAsync(new Uri("/admin/v1/webhooks/dlq/purge-all", UriKind.Relative), null);
        purgeRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify all dead-letters purged
        using (var verifyScope2 = _factory.Services.CreateScope())
        {
            var verifyDb2 = verifyScope2.ServiceProvider.GetRequiredService<AchillesDbContext>();
            var deadLetterCount = await verifyDb2.WebhookDeliveries.CountAsync(d => d.Status == "dead_letter");
            deadLetterCount.Should().Be(0);
        }
    }
}
