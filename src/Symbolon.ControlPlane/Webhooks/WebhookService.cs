using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Symbolon.ControlPlane.Models;
using Symbolon.Data;
using Symbolon.Data.Entities;

namespace Symbolon.ControlPlane.Webhooks;

public interface IWebhookDispatcher
{
    Task PublishEventAsync(string eventType, object payload, string? tenantId = null, CancellationToken ct = default);
    Task<WebhookTestResultDto> TestPingAsync(string subscriptionId, CancellationToken ct = default);
}

#pragma warning disable CA1031 // Do not catch general exception types (resilient background webhook delivery)
public sealed class WebhookDispatcher : IWebhookDispatcher
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly HttpClient _httpClient;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WebhookDispatcher> _logger;

    public WebhookDispatcher(
        IServiceScopeFactory scopeFactory,
        HttpClient httpClient,
        TimeProvider timeProvider,
        ILogger<WebhookDispatcher> logger)
    {
        _scopeFactory = scopeFactory;
        _httpClient = httpClient;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task PublishEventAsync(string eventType, object payload, string? tenantId = null, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentNullException.ThrowIfNull(payload);

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SymbolonDbContext>();

            var query = db.WebhookSubscriptions.Where(s => s.IsActive);
            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                query = query.Where(s => s.TenantId == tenantId);
            }

            var subscriptions = await query.ToListAsync(ct).ConfigureAwait(false);
            if (subscriptions.Count == 0)
            {
                return;
            }

            var now = _timeProvider.GetUtcNow();
            var envelope = new
            {
                id = $"evt_{Guid.NewGuid():N}",
                type = eventType,
                timestamp = now,
                data = payload
            };

            string payloadJson = JsonSerializer.Serialize(envelope, JsonOptions);

            foreach (var sub in subscriptions)
            {
                // Check if subscribed
                var subscribedEvents = JsonSerializer.Deserialize<List<string>>(sub.EventsJson) ?? [];
                if (!subscribedEvents.Contains("*") && !subscribedEvents.Contains(eventType, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                string deliveryId = $"del_{Guid.NewGuid():N}";
                var delivery = new WebhookDeliveryEntity
                {
                    Id = deliveryId,
                    SubscriptionId = sub.Id,
                    EventType = eventType,
                    PayloadJson = payloadJson,
                    Status = "pending",
                    Attempts = 1,
                    CreatedAt = now
                };

                db.WebhookDeliveries.Add(delivery);

                try
                {
                    long unixSeconds = now.ToUnixTimeSeconds();
                    string signature = ComputeSignature(sub.Secret, unixSeconds, payloadJson);

                    using var request = new HttpRequestMessage(HttpMethod.Post, sub.Url);
                    request.Headers.Add("User-Agent", "Symbolon-Webhook/1.0");
                    request.Headers.Add("X-Symbolon-Event", eventType);
                    request.Headers.Add("X-Symbolon-Delivery", deliveryId);
                    request.Headers.Add("X-Symbolon-Timestamp", unixSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    request.Headers.Add("X-Symbolon-Signature", $"sha256={signature}");
                    request.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

                    using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
                    delivery.StatusCode = (int)response.StatusCode;
                    if (response.IsSuccessStatusCode)
                    {
                        delivery.Status = "delivered";
                        delivery.DeliveredAt = _timeProvider.GetUtcNow();
                    }
                    else
                    {
                        delivery.Status = "failed";
                        delivery.LastError = $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}";
                        delivery.NextAttemptAt = _timeProvider.GetUtcNow().AddMinutes(1);
                    }
                }
                catch (Exception ex)
                {
                    delivery.Status = "failed";
                    delivery.LastError = ex.Message;
                    delivery.NextAttemptAt = _timeProvider.GetUtcNow().AddMinutes(1);
                    _logger.LogWarning(ex, "Failed to deliver webhook {DeliveryId} to {Url}", deliveryId, sub.Url);
                }

                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing webhooks for event {EventType}", eventType);
        }
    }

    public async Task<WebhookTestResultDto> TestPingAsync(string subscriptionId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionId);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SymbolonDbContext>();

        var sub = await db.WebhookSubscriptions.FirstOrDefaultAsync(s => s.Id == subscriptionId, ct).ConfigureAwait(false);
        if (sub is null)
        {
            return new WebhookTestResultDto(false, null, null, 0, "Webhook subscription not found.");
        }

        var now = _timeProvider.GetUtcNow();
        var envelope = new
        {
            id = $"evt_{Guid.NewGuid():N}",
            type = "test.ping",
            timestamp = now,
            data = new
            {
                message = "Symbolon test webhook ping",
                subscriptionId = sub.Id,
                url = sub.Url
            }
        };

        string payloadJson = JsonSerializer.Serialize(envelope, JsonOptions);
        long unixSeconds = now.ToUnixTimeSeconds();
        string signature = ComputeSignature(sub.Secret, unixSeconds, payloadJson);

        var sw = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, sub.Url);
            request.Headers.Add("User-Agent", "Symbolon-Webhook/1.0");
            request.Headers.Add("X-Symbolon-Event", "test.ping");
            request.Headers.Add("X-Symbolon-Delivery", $"del_test_{Guid.NewGuid():N}");
            request.Headers.Add("X-Symbolon-Timestamp", unixSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            request.Headers.Add("X-Symbolon-Signature", $"sha256={signature}");
            request.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            sw.Stop();
            string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            return new WebhookTestResultDto(
                response.IsSuccessStatusCode,
                (int)response.StatusCode,
                body.Length > 500 ? body[..500] : body,
                sw.ElapsedMilliseconds,
                response.IsSuccessStatusCode ? null : $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
        }
        catch (Exception ex)
        {
            sw.Stop();
            return new WebhookTestResultDto(
                false,
                null,
                null,
                sw.ElapsedMilliseconds,
                ex.Message);
        }
    }

    public static string ComputeSignature(string secret, long timestamp, string payloadJson)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentNullException.ThrowIfNull(payloadJson);

        string stringToSign = $"{timestamp}.{payloadJson}";
        byte[] keyBytes = Encoding.UTF8.GetBytes(secret);
        byte[] dataBytes = Encoding.UTF8.GetBytes(stringToSign);

        byte[] hash = HMACSHA256.HashData(keyBytes, dataBytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
#pragma warning restore CA1031
