using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Symbolon.ControlPlane.Models;
using Symbolon.ControlPlane.Webhooks;
using Symbolon.Data;
using Symbolon.Data.Entities;

namespace Symbolon.ControlPlane.Endpoints;

public static class WebhookEndpoints
{
    public static RouteGroupBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/v1/webhooks").WithTags("Webhooks");

        group.MapPost("/", CreateWebhookAsync).WithName("CreateWebhook");
        group.MapGet("/", GetWebhooksAsync).WithName("GetWebhooks");
        group.MapGet("/{id}", GetWebhookByIdAsync).WithName("GetWebhookById");
        group.MapDelete("/{id}", DeleteWebhookAsync).WithName("DeleteWebhook");
        group.MapPost("/{id}/test", TestWebhookAsync).WithName("TestWebhook");
        group.MapGet("/{id}/deliveries", GetWebhookDeliveriesAsync).WithName("GetWebhookDeliveries");

        return group;
    }

    private static async Task<IResult> CreateWebhookAsync(
        CreateWebhookDto dto,
        HttpContext context,
        SymbolonDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.Url) || !Uri.TryCreate(dto.Url, UriKind.Absolute, out var parsedUri) ||
            (parsedUri.Scheme != Uri.UriSchemeHttp && parsedUri.Scheme != Uri.UriSchemeHttps))
        {
            return TypedResults.BadRequest("A valid HTTP or HTTPS URL is required.");
        }

        string tenantId = context.Request.Headers["X-Tenant-Id"].FirstOrDefault()
            ?? (await db.Tenants.Select(t => t.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false))
            ?? "ten_default";

        string secret = string.IsNullOrWhiteSpace(dto.Secret)
            ? $"whsec_{Convert.ToHexString(RandomNumberGenerator.GetBytes(24)).ToLowerInvariant()}"
            : dto.Secret.Trim();

        var events = dto.Events is not null && dto.Events.Count > 0
            ? dto.Events
            : ["*"];

        string id = $"whk_{Guid.NewGuid():N}";
        var subscription = new WebhookSubscriptionEntity
        {
            Id = id,
            TenantId = tenantId,
            Url = dto.Url.Trim(),
            Secret = secret,
            EventsJson = JsonSerializer.Serialize(events),
            IsActive = true,
            CreatedAt = time.GetUtcNow()
        };

        db.WebhookSubscriptions.Add(subscription);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return TypedResults.Created($"/admin/v1/webhooks/{id}", new WebhookSubscriptionDto(
            subscription.Id,
            subscription.TenantId,
            subscription.Url,
            events,
            subscription.IsActive,
            subscription.CreatedAt));
    }

    private static async Task<IResult> GetWebhooksAsync(
        HttpContext context,
        SymbolonDbContext db,
        CancellationToken ct)
    {
        string? tenantId = context.Request.Headers["X-Tenant-Id"].FirstOrDefault();
        var query = db.WebhookSubscriptions.AsQueryable();
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            query = query.Where(w => w.TenantId == tenantId);
        }

        var list = await query
            .OrderByDescending(w => w.CreatedAt)
            .Select(w => new
            {
                w.Id,
                w.TenantId,
                w.Url,
                w.EventsJson,
                w.IsActive,
                w.CreatedAt
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var result = list.Select(w => new WebhookSubscriptionDto(
            w.Id,
            w.TenantId,
            w.Url,
            JsonSerializer.Deserialize<List<string>>(w.EventsJson) ?? ["*"],
            w.IsActive,
            w.CreatedAt)).ToList();

        return TypedResults.Ok(result);
    }

    private static async Task<IResult> GetWebhookByIdAsync(
        string id,
        SymbolonDbContext db,
        CancellationToken ct)
    {
        var w = await db.WebhookSubscriptions.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (w is null) return TypedResults.NotFound();

        return TypedResults.Ok(new WebhookSubscriptionDto(
            w.Id,
            w.TenantId,
            w.Url,
            JsonSerializer.Deserialize<List<string>>(w.EventsJson) ?? ["*"],
            w.IsActive,
            w.CreatedAt));
    }

    private static async Task<IResult> DeleteWebhookAsync(
        string id,
        SymbolonDbContext db,
        CancellationToken ct)
    {
        var w = await db.WebhookSubscriptions.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (w is null) return TypedResults.NotFound();

        db.WebhookSubscriptions.Remove(w);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return TypedResults.Ok(new { message = $"Webhook subscription {id} deleted." });
    }

    private static async Task<IResult> TestWebhookAsync(
        string id,
        IWebhookDispatcher dispatcher,
        CancellationToken ct)
    {
        var result = await dispatcher.TestPingAsync(id, ct).ConfigureAwait(false);
        return TypedResults.Ok(result);
    }

    private static async Task<IResult> GetWebhookDeliveriesAsync(
        string id,
        int? limit,
        SymbolonDbContext db,
        CancellationToken ct)
    {
        int max = limit ?? 20;
        var deliveries = await db.WebhookDeliveries
            .Where(d => d.SubscriptionId == id)
            .OrderByDescending(d => d.CreatedAt)
            .Take(max)
            .Select(d => new WebhookDeliveryDto(
                d.Id,
                d.SubscriptionId,
                d.EventType,
                d.Status,
                d.StatusCode,
                d.Attempts,
                d.DeliveredAt,
                d.LastError,
                d.CreatedAt))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return TypedResults.Ok(deliveries);
    }
}
