using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Symbolon.ControlPlane.Models;
using Symbolon.Data;
using Symbolon.Data.Entities;

namespace Symbolon.ControlPlane.Webhooks;

public interface IWebhookDispatcher
{
    Task PublishEventAsync(string eventType, object payload, string? tenantId = null, CancellationToken ct = default);
    Task<WebhookTestResultDto> TestPingAsync(string subscriptionId, CancellationToken ct = default);
}

#pragma warning disable CA1054, CA1031 // Uri parameter and exception catching for resilient network resolution
public static class WebhookSecurityValidator
{
    public static bool IsSafeWebhookUrl(string? url, bool isDevelopment, bool allowLocalWebhooks = false)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)) return false;

        // Scheme check: in production, require HTTPS
        if (!isDevelopment && !allowLocalWebhooks && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        string host = uri.DnsSafeHost;
        if (string.IsNullOrWhiteSpace(host)) return false;

        // Block well-known cloud metadata hostnames directly
        if (string.Equals(host, "metadata.google.internal", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(host, "instance-data", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        IPAddress[] addresses;
        if (IPAddress.TryParse(host, out var parsedIp))
        {
            addresses = [parsedIp];
        }
        else
        {
            try
            {
                addresses = Dns.GetHostAddresses(host);
            }
            catch
            {
                return false;
            }
        }

        if (addresses.Length == 0) return false;

        foreach (var ip in addresses)
        {
            if (IsDisallowedIp(ip, isDevelopment, allowLocalWebhooks))
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsDisallowedIp(IPAddress ip, bool isDevelopment, bool allowLocalWebhooks)
    {
        if (ip.IsIPv4MappedToIPv6)
        {
            ip = ip.MapToIPv4();
        }

        byte[] bytes = ip.GetAddressBytes();
        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            // 169.254.x.x (AWS / Azure / GCP IMDS link-local metadata) - ALWAYS BLOCKED
            if (bytes[0] == 169 && bytes[1] == 254) return true;
            // 0.0.0.0/8
            if (bytes[0] == 0) return true;
            // 255.255.255.255
            if (bytes[0] == 255) return true;
            // 224.0.0.0/4 multicast
            if (bytes[0] >= 224 && bytes[0] <= 239) return true;

            // Loopback 127.0.0.0/8
            if (bytes[0] == 127)
            {
                return !isDevelopment && !allowLocalWebhooks;
            }

            // Private RFC 1918 & CGNAT
            bool isPrivate =
                bytes[0] == 10 || // 10.0.0.0/8
                (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) || // 172.16.0.0/12
                (bytes[0] == 192 && bytes[1] == 168) || // 192.168.0.0/16
                (bytes[0] == 100 && bytes[1] >= 64 && bytes[1] <= 127); // 100.64.0.0/10 Carrier-grade NAT

            if (isPrivate && !isDevelopment && !allowLocalWebhooks)
            {
                return true;
            }
        }
        else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (IPAddress.IsLoopback(ip))
            {
                return !isDevelopment && !allowLocalWebhooks;
            }
            if (ip.IsIPv6LinkLocal || ip.IsIPv6Multicast || ip.IsIPv6SiteLocal)
            {
                return true;
            }
            // fc00::/7 (Unique Local Address)
            if ((bytes[0] & 0xFE) == 0xFC && !isDevelopment && !allowLocalWebhooks)
            {
                return true;
            }
        }

        return false;
    }
}
#pragma warning restore CA1054

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
    private readonly bool _isDevelopment;
    private readonly bool _allowLocalWebhooks;

    public WebhookDispatcher(
        IServiceScopeFactory scopeFactory,
        HttpClient httpClient,
        TimeProvider timeProvider,
        ILogger<WebhookDispatcher> logger,
        IHostEnvironment env,
        IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _httpClient = httpClient;
        _timeProvider = timeProvider;
        _logger = logger;
        _isDevelopment = env.IsDevelopment();
        _allowLocalWebhooks = configuration.GetValue<bool>("Security:AllowLocalWebhooks");
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

                if (!WebhookSecurityValidator.IsSafeWebhookUrl(sub.Url, _isDevelopment, _allowLocalWebhooks))
                {
                    delivery.Status = "failed";
                    delivery.LastError = "SSRF safety check failed: target URL not permitted.";
                    _logger.LogWarning("Webhook delivery {DeliveryId} skipped: URL {Url} violates network security policy.", deliveryId, sub.Url);
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                    continue;
                }

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

        if (!WebhookSecurityValidator.IsSafeWebhookUrl(sub.Url, _isDevelopment, _allowLocalWebhooks))
        {
            return new WebhookTestResultDto(
                false,
                400,
                null,
                0,
                "Webhook destination URL is not permitted (SSRF protection policy).");
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
