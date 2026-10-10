using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Achilles.Data.Entities;
using Achilles.Domain.Webhooks;

namespace Achilles.Data.Stores;

public sealed class EfWebhookStore : IWebhookStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly AchillesDbContext _db;
    private readonly TimeProvider _timeProvider;

    public EfWebhookStore(AchillesDbContext db, TimeProvider timeProvider)
    {
        _db = db;
        _timeProvider = timeProvider;
    }

    public async Task<IReadOnlyList<WebhookSubscriptionModel>> GetSubscriptionsAsync(
        string? tenantId,
        string? eventType = null,
        CancellationToken ct = default)
    {
        var query = _db.WebhookSubscriptions.AsNoTracking().Where(s => s.IsActive);
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            query = query.Where(s => s.TenantId == tenantId);
        }

        var entities = await query.ToListAsync(ct).ConfigureAwait(false);
        var results = new List<WebhookSubscriptionModel>();

        foreach (var entity in entities)
        {
            var events = ParseEvents(entity.EventsJson);
            if (!string.IsNullOrWhiteSpace(eventType))
            {
                bool matches = events.Contains(WebhookEventTypes.Wildcard) ||
                               events.Contains(eventType, StringComparer.OrdinalIgnoreCase);
                if (!matches) continue;
            }

            results.Add(ToModel(entity, events));
        }

        return results;
    }

    public async Task<WebhookSubscriptionModel?> GetSubscriptionByIdAsync(string id, CancellationToken ct = default)
    {
        var entity = await _db.WebhookSubscriptions.AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == id, ct)
            .ConfigureAwait(false);

        return entity is null ? null : ToModel(entity, ParseEvents(entity.EventsJson));
    }

    public async Task<WebhookSubscriptionModel> CreateSubscriptionAsync(
        string tenantId,
        string name,
        string url,
        string secret,
        string format,
        IReadOnlyList<string> events,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        var eventsList = events is not null && events.Count > 0 ? events : [WebhookEventTypes.Wildcard];
        string id = $"whk_{Guid.NewGuid():N}";
        string cleanFormat = string.IsNullOrWhiteSpace(format) ? "json" : format.Trim().ToUpperInvariant();

        var entity = new WebhookSubscriptionEntity
        {
            Id = id,
            TenantId = tenantId,
            Name = string.IsNullOrWhiteSpace(name) ? url.Trim() : name.Trim(),
            Url = url.Trim(),
            Secret = secret,
            Format = cleanFormat,
            EventsJson = JsonSerializer.Serialize(eventsList, JsonOptions),
            IsActive = true,
            FailureCount = 0,
            CreatedAt = _timeProvider.GetUtcNow()
        };

        _db.WebhookSubscriptions.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return ToModel(entity, eventsList);
    }

    public async Task<bool> DeleteSubscriptionAsync(string id, string? tenantId = null, CancellationToken ct = default)
    {
        var query = _db.WebhookSubscriptions.Where(s => s.Id == id);
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            query = query.Where(s => s.TenantId == tenantId);
        }

        var entity = await query.FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (entity is null) return false;

        _db.WebhookSubscriptions.Remove(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    public async Task LogDeliveryAsync(WebhookDeliveryModel delivery, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(delivery);

        var entity = await _db.WebhookDeliveries.FirstOrDefaultAsync(d => d.Id == delivery.Id, ct).ConfigureAwait(false);
        if (entity is null)
        {
            entity = new WebhookDeliveryEntity
            {
                Id = delivery.Id,
                SubscriptionId = delivery.SubscriptionId,
                EventType = delivery.EventType,
                PayloadJson = delivery.PayloadJson,
                Status = delivery.Status,
                StatusCode = delivery.StatusCode,
                DurationMs = delivery.DurationMs,
                Attempts = delivery.Attempts,
                DeliveredAt = delivery.DeliveredAt,
                LastError = delivery.LastError,
                CreatedAt = delivery.CreatedAt
            };
            _db.WebhookDeliveries.Add(entity);
        }
        else
        {
            entity.Status = delivery.Status;
            entity.StatusCode = delivery.StatusCode;
            entity.DurationMs = delivery.DurationMs;
            entity.Attempts = delivery.Attempts;
            entity.DeliveredAt = delivery.DeliveredAt;
            entity.LastError = delivery.LastError;
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<WebhookDeliveryModel>> GetDeliveriesAsync(
        string? subscriptionId = null,
        string? status = null,
        int limit = 50,
        CancellationToken ct = default)
    {
        var query = _db.WebhookDeliveries.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(subscriptionId))
        {
            query = query.Where(d => d.SubscriptionId == subscriptionId);
        }

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

        int max = Math.Clamp(limit, 1, 500);

        var list = await query
            .OrderByDescending(d => d.CreatedAt)
            .Take(max)
            .Select(d => new WebhookDeliveryModel(
                d.Id,
                d.SubscriptionId,
                d.EventType,
                d.PayloadJson,
                d.Status,
                d.StatusCode,
                d.DurationMs,
                d.Attempts,
                d.DeliveredAt,
                d.LastError,
                d.CreatedAt))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return list;
    }

    public async Task<WebhookDeliveryModel?> GetDeliveryByIdAsync(string id, CancellationToken ct = default)
    {
        var d = await _db.WebhookDeliveries.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == id, ct)
            .ConfigureAwait(false);

        return d is null ? null : new WebhookDeliveryModel(
            d.Id,
            d.SubscriptionId,
            d.EventType,
            d.PayloadJson,
            d.Status,
            d.StatusCode,
            d.DurationMs,
            d.Attempts,
            d.DeliveredAt,
            d.LastError,
            d.CreatedAt);
    }

    public async Task UpdateSubscriptionFailureAsync(string subscriptionId, bool success, CancellationToken ct = default)
    {
        var sub = await _db.WebhookSubscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId, ct).ConfigureAwait(false);
        if (sub is null) return;

        if (success)
        {
            sub.FailureCount = 0;
            sub.LastDeliveredAt = _timeProvider.GetUtcNow();
        }
        else
        {
            sub.FailureCount++;
            if (sub.FailureCount >= 10)
            {
                sub.IsActive = false;
            }
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    private static List<string> ParseEvents(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [WebhookEventTypes.Wildcard];
        try
        {
            return JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [WebhookEventTypes.Wildcard];
        }
        catch (JsonException)
        {
            return [WebhookEventTypes.Wildcard];
        }
    }

    private static WebhookSubscriptionModel ToModel(WebhookSubscriptionEntity entity, IReadOnlyList<string> events)
    {
        return new WebhookSubscriptionModel(
            entity.Id,
            entity.TenantId,
            entity.Name,
            entity.Url,
            entity.Secret,
            entity.Format,
            events,
            entity.IsActive,
            entity.FailureCount,
            entity.LastDeliveredAt,
            entity.CreatedAt);
    }
}
