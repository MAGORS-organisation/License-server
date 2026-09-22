using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Symbolon.ControlPlane.Models;
using Symbolon.ControlPlane.Webhooks;
using Symbolon.Data;
using Symbolon.Data.Entities;
using Symbolon.Domain.Webhooks;

namespace Symbolon.ControlPlane.Endpoints;

public static class WebhookEndpoints
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static RouteGroupBuilder MapWebhookEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/v1/webhooks").WithTags("Webhooks");

        group.MapPost("/", CreateWebhookAsync).WithName("CreateWebhook");
        group.MapGet("/", GetWebhooksAsync).WithName("GetWebhooks");
        group.MapGet("/deliveries", GetAllWebhookDeliveriesAsync).WithName("GetAllWebhookDeliveries");
        group.MapPost("/deliveries/{id}/replay", ReplayWebhookDeliveryAsync).WithName("ReplayWebhookDelivery");
        group.MapGet("/{id}", GetWebhookByIdAsync).WithName("GetWebhookById");
        group.MapDelete("/{id}", DeleteWebhookAsync).WithName("DeleteWebhook");
        group.MapPost("/{id}/test", TestWebhookAsync).WithName("TestWebhook");
        group.MapGet("/{id}/deliveries", GetWebhookDeliveriesAsync).WithName("GetWebhookDeliveries");

        return group;
    }

    private static string? GetTenantFilter(HttpContext context)
    {
        bool isSuper = context.User.IsInRole("admin:super");
        if (isSuper)
        {
            return context.Request.Headers["X-Tenant-Id"].FirstOrDefault()
                ?? context.User.FindFirst("tenant_id")?.Value;
        }
        return context.User.FindFirst("tenant_id")?.Value;
    }

    private static async Task<IResult> CreateWebhookAsync(
        CreateWebhookDto dto,
        HttpContext context,
        SymbolonDbContext db,
        TimeProvider time,
        IHostEnvironment env,
        IConfiguration config,
        CancellationToken ct)
    {
        bool isDev = env.IsDevelopment();
        bool allowLocal = config.GetValue<bool>("Security:AllowLocalWebhooks");

        if (!WebhookSecurityValidator.IsSafeWebhookUrl(dto.Url, isDev, allowLocal))
        {
            return TypedResults.BadRequest("The webhook target URL is invalid or violates network security policy (SSRF protection).");
        }

        bool isSuper = context.User.IsInRole("admin:super");
        string? tenantId = isSuper ? context.Request.Headers["X-Tenant-Id"].FirstOrDefault() : null;
        tenantId ??= context.User.FindFirst("tenant_id")?.Value
            ?? (await db.Tenants.Select(t => t.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false))
            ?? "ten_default";

        string secret = string.IsNullOrWhiteSpace(dto.Secret)
            ? $"whsec_{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(24))}"
            : dto.Secret.Trim();

        var events = dto.Events is not null && dto.Events.Count > 0
            ? dto.Events
            : ["*"];

        string id = $"whk_{Guid.NewGuid():N}";
        string cleanFormat = string.IsNullOrWhiteSpace(dto.Format) ? "json" : dto.Format.Trim().ToUpperInvariant();
        string cleanName = string.IsNullOrWhiteSpace(dto.Name) ? dto.Url.Trim() : dto.Name.Trim();

        var subscription = new WebhookSubscriptionEntity
        {
            Id = id,
            TenantId = tenantId,
            Name = cleanName,
            Url = dto.Url.Trim(),
            Secret = secret,
            Format = cleanFormat,
            EventsJson = JsonSerializer.Serialize(events, JsonOptions),
            IsActive = true,
            FailureCount = 0,
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
            subscription.CreatedAt,
            subscription.Name,
            subscription.Format,
            subscription.FailureCount,
            subscription.LastDeliveredAt));
    }

    private static async Task<IResult> GetWebhooksAsync(
        HttpContext context,
        SymbolonDbContext db,
        CancellationToken ct)
    {
        string? tenantId = GetTenantFilter(context);
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
                w.Name,
                w.Url,
                w.Format,
                w.EventsJson,
                w.IsActive,
                w.FailureCount,
                w.LastDeliveredAt,
                w.CreatedAt
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var result = list.Select(w => new WebhookSubscriptionDto(
            w.Id,
            w.TenantId,
            w.Url,
            JsonSerializer.Deserialize<List<string>>(w.EventsJson, JsonOptions) ?? ["*"],
            w.IsActive,
            w.CreatedAt,
            w.Name,
            w.Format,
            w.FailureCount,
            w.LastDeliveredAt)).ToList();

        return TypedResults.Ok(result);
    }

    private static async Task<IResult> GetWebhookByIdAsync(
        string id,
        HttpContext context,
        SymbolonDbContext db,
        CancellationToken ct)
    {
        string? tenantId = GetTenantFilter(context);
        var query = db.WebhookSubscriptions.AsQueryable();
        if (!string.IsNullOrWhiteSpace(tenantId) && !context.User.IsInRole("admin:super"))
        {
            query = query.Where(x => x.TenantId == tenantId);
        }

        var w = await query.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (w is null) return TypedResults.NotFound();

        return TypedResults.Ok(new WebhookSubscriptionDto(
            w.Id,
            w.TenantId,
            w.Url,
            JsonSerializer.Deserialize<List<string>>(w.EventsJson, JsonOptions) ?? ["*"],
            w.IsActive,
            w.CreatedAt,
            w.Name,
            w.Format,
            w.FailureCount,
            w.LastDeliveredAt));
    }

    private static async Task<IResult> DeleteWebhookAsync(
        string id,
        HttpContext context,
        SymbolonDbContext db,
        CancellationToken ct)
    {
        string? tenantId = GetTenantFilter(context);
        var query = db.WebhookSubscriptions.AsQueryable();
        if (!string.IsNullOrWhiteSpace(tenantId) && !context.User.IsInRole("admin:super"))
        {
            query = query.Where(x => x.TenantId == tenantId);
        }

        var w = await query.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (w is null) return TypedResults.NotFound();

        db.WebhookSubscriptions.Remove(w);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return TypedResults.Ok(new { message = $"Webhook subscription {id} deleted." });
    }

    private static async Task<IResult> TestWebhookAsync(
        string id,
        HttpContext context,
        SymbolonDbContext db,
        IWebhookDispatcher dispatcher,
        CancellationToken ct)
    {
        string? tenantId = GetTenantFilter(context);
        var query = db.WebhookSubscriptions.AsQueryable();
        if (!string.IsNullOrWhiteSpace(tenantId) && !context.User.IsInRole("admin:super"))
        {
            query = query.Where(x => x.TenantId == tenantId);
        }

        var exists = await query.AnyAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (!exists) return TypedResults.NotFound();

        var result = await dispatcher.TestPingAsync(id, ct).ConfigureAwait(false);
        return TypedResults.Ok(result);
    }

    private static async Task<IResult> GetWebhookDeliveriesAsync(
        string id,
        int? limit,
        HttpContext context,
        SymbolonDbContext db,
        CancellationToken ct)
    {
        string? tenantId = GetTenantFilter(context);
        var query = db.WebhookSubscriptions.AsQueryable();
        if (!string.IsNullOrWhiteSpace(tenantId) && !context.User.IsInRole("admin:super"))
        {
            query = query.Where(x => x.TenantId == tenantId);
        }

        var exists = await query.AnyAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (!exists) return TypedResults.NotFound();

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
                d.CreatedAt,
                d.DurationMs))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return TypedResults.Ok(deliveries);
    }

    private static async Task<IResult> GetAllWebhookDeliveriesAsync(
        string? status,
        int? limit,
        SymbolonDbContext db,
        CancellationToken ct)
    {
        int max = Math.Clamp(limit ?? 50, 1, 200);
        var query = db.WebhookDeliveries.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(status))
        {
            string clean = status.Trim();
            string normalized = clean switch
            {
                _ when string.Equals(clean, "delivered", StringComparison.OrdinalIgnoreCase) => "delivered",
                _ when string.Equals(clean, "failed", StringComparison.OrdinalIgnoreCase) => "failed",
                _ when string.Equals(clean, "dead_letter", StringComparison.OrdinalIgnoreCase) => "dead_letter",
                _ when string.Equals(clean, "pending", StringComparison.OrdinalIgnoreCase) => "pending",
                _ => clean
            };
            query = query.Where(d => d.Status == normalized);
        }

        var list = await query
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
                d.CreatedAt,
                d.DurationMs))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return TypedResults.Ok(list);
    }

    private static async Task<IResult> ReplayWebhookDeliveryAsync(
        string id,
        IWebhookDispatcher dispatcher,
        CancellationToken ct)
    {
        var result = await dispatcher.ReplayDeliveryAsync(id, ct).ConfigureAwait(false);
        if (result is null)
        {
            return TypedResults.NotFound(new { message = $"Delivery {id} not found or subscription is missing." });
        }

        return TypedResults.Ok(result);
    }
}
