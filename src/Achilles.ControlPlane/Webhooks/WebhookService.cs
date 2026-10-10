using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Achilles.ControlPlane.Models;
using Achilles.Data;
using Achilles.Data.Entities;
using Achilles.Domain.Webhooks;

namespace Achilles.ControlPlane.Webhooks;

public interface IWebhookDispatcher
{
    Task PublishEventAsync(string eventType, object payload, string? tenantId = null, CancellationToken ct = default);
    Task<WebhookTestResultDto> TestPingAsync(string subscriptionId, CancellationToken ct = default);
    Task<WebhookDeliveryDto?> ReplayDeliveryAsync(string deliveryId, CancellationToken ct = default);
    Task<int> ProcessPendingRetriesAsync(CancellationToken ct = default);
}

public static class WebhookBackoffHelper
{
    public const int MaxAttempts = 5;
    private static readonly TimeSpan BaseDelay = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan MaxDelay = TimeSpan.FromSeconds(300);

    public static TimeSpan CalculateBackoff(int attempt)
    {
        double exponentialSeconds = BaseDelay.TotalSeconds * Math.Pow(2, Math.Max(0, attempt - 1));
        double cappedSeconds = Math.Min(exponentialSeconds, MaxDelay.TotalSeconds);
        double jitterSeconds = RandomNumberGenerator.GetInt32(0, 1000) / 1000.0;
        return TimeSpan.FromSeconds(cappedSeconds + jitterSeconds);
    }
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
            var db = scope.ServiceProvider.GetRequiredService<AchillesDbContext>();

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
            string eventId = $"evt_{Guid.NewGuid():N}";
            var domainEvt = new WebhookEvent(
                eventId,
                eventType,
                tenantId ?? "ten_default",
                now,
                payload);

            foreach (var sub in subscriptions)
            {
                // Check if subscribed
                var subscribedEvents = JsonSerializer.Deserialize<List<string>>(sub.EventsJson, JsonOptions) ?? [];
                if (!subscribedEvents.Contains("*") && !subscribedEvents.Contains(eventType, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                string payloadJson = WebhookAdapters.FormatPayload(sub.Format, domainEvt, sub.Name);

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

                var sw = Stopwatch.StartNew();
                try
                {
                    long unixSeconds = now.ToUnixTimeSeconds();
                    string signature = WebhookSecurity.ComputeSignature(sub.Secret, unixSeconds, payloadJson);
                    string sigHeader = WebhookSecurity.BuildSignatureHeader(sub.Secret, unixSeconds, payloadJson);

                    using var request = new HttpRequestMessage(HttpMethod.Post, sub.Url);
                    request.Headers.Add("User-Agent", "Symbolon-Webhook/1.0");
                    request.Headers.Add("X-Symbolon-Event", eventType);
                    request.Headers.Add("X-Symbolon-Delivery", deliveryId);
                    request.Headers.Add("X-Symbolon-Timestamp", unixSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    request.Headers.Add("X-Symbolon-Signature", sigHeader);
                    request.Headers.Add("X-Symbolon-Signature-256", $"sha256={signature}");
                    request.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

                    using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
                    sw.Stop();
                    delivery.DurationMs = sw.ElapsedMilliseconds;
                    delivery.StatusCode = (int)response.StatusCode;

                    if (response.IsSuccessStatusCode)
                    {
                        delivery.Status = "delivered";
                        delivery.DeliveredAt = _timeProvider.GetUtcNow();
                        delivery.NextAttemptAt = null;
                        sub.FailureCount = 0;
                        sub.LastDeliveredAt = delivery.DeliveredAt;
                    }
                    else
                    {
                        delivery.LastError = $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}";
                        if (delivery.Attempts >= WebhookBackoffHelper.MaxAttempts)
                        {
                            delivery.Status = "dead_letter";
                            delivery.NextAttemptAt = null;
                        }
                        else
                        {
                            delivery.Status = "failed";
                            delivery.NextAttemptAt = now + WebhookBackoffHelper.CalculateBackoff(delivery.Attempts);
                        }
                        sub.FailureCount++;
                        if (sub.FailureCount >= 10) sub.IsActive = false;
                    }
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    delivery.DurationMs = sw.ElapsedMilliseconds;
                    delivery.LastError = ex.Message;
                    if (delivery.Attempts >= WebhookBackoffHelper.MaxAttempts)
                    {
                        delivery.Status = "dead_letter";
                        delivery.NextAttemptAt = null;
                    }
                    else
                    {
                        delivery.Status = "failed";
                        delivery.NextAttemptAt = now + WebhookBackoffHelper.CalculateBackoff(delivery.Attempts);
                    }
                    sub.FailureCount++;
                    if (sub.FailureCount >= 10) sub.IsActive = false;
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
        var db = scope.ServiceProvider.GetRequiredService<AchillesDbContext>();

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
        var domainEvt = new WebhookEvent(
            $"evt_test_{Guid.NewGuid():N}",
            WebhookEventTypes.TestPing,
            sub.TenantId,
            now,
            new
            {
                message = "Symbolon test webhook ping",
                subscriptionId = sub.Id,
                url = sub.Url,
                name = sub.Name,
                format = sub.Format
            });

        string payloadJson = WebhookAdapters.FormatPayload(sub.Format, domainEvt, sub.Name);
        long unixSeconds = now.ToUnixTimeSeconds();
        string signature = WebhookSecurity.ComputeSignature(sub.Secret, unixSeconds, payloadJson);
        string sigHeader = WebhookSecurity.BuildSignatureHeader(sub.Secret, unixSeconds, payloadJson);

        var sw = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, sub.Url);
            request.Headers.Add("User-Agent", "Symbolon-Webhook/1.0");
            request.Headers.Add("X-Symbolon-Event", WebhookEventTypes.TestPing);
            request.Headers.Add("X-Symbolon-Delivery", $"del_test_{Guid.NewGuid():N}");
            request.Headers.Add("X-Symbolon-Timestamp", unixSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            request.Headers.Add("X-Symbolon-Signature", sigHeader);
            request.Headers.Add("X-Symbolon-Signature-256", $"sha256={signature}");
            request.Content = new StringContent(payloadJson, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            sw.Stop();
            string body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                sub.FailureCount = 0;
                sub.LastDeliveredAt = _timeProvider.GetUtcNow();
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }

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

    public async Task<WebhookDeliveryDto?> ReplayDeliveryAsync(string deliveryId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deliveryId);

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AchillesDbContext>();

        var delivery = await db.WebhookDeliveries
            .Include(d => d.Subscription)
            .FirstOrDefaultAsync(d => d.Id == deliveryId, ct)
            .ConfigureAwait(false);

        if (delivery is null || delivery.Subscription is null)
        {
            return null;
        }

        var sub = delivery.Subscription;
        if (!WebhookSecurityValidator.IsSafeWebhookUrl(sub.Url, _isDevelopment, _allowLocalWebhooks))
        {
            delivery.Status = "failed";
            delivery.LastError = "SSRF security check failed for target URL.";
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return ToDeliveryDto(delivery);
        }

        var now = _timeProvider.GetUtcNow();
        long unixSeconds = now.ToUnixTimeSeconds();
        string signature = WebhookSecurity.ComputeSignature(sub.Secret, unixSeconds, delivery.PayloadJson);
        string sigHeader = WebhookSecurity.BuildSignatureHeader(sub.Secret, unixSeconds, delivery.PayloadJson);

        delivery.Attempts++;
        var sw = Stopwatch.StartNew();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, sub.Url);
            request.Headers.Add("User-Agent", "Symbolon-Webhook/1.0");
            request.Headers.Add("X-Symbolon-Event", delivery.EventType);
            request.Headers.Add("X-Symbolon-Delivery", delivery.Id);
            request.Headers.Add("X-Symbolon-Timestamp", unixSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
            request.Headers.Add("X-Symbolon-Signature", sigHeader);
            request.Headers.Add("X-Symbolon-Signature-256", $"sha256={signature}");
            request.Headers.Add("X-Symbolon-Replay", "true");
            request.Content = new StringContent(delivery.PayloadJson, Encoding.UTF8, "application/json");

            using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
            sw.Stop();
            delivery.DurationMs = sw.ElapsedMilliseconds;
            delivery.StatusCode = (int)response.StatusCode;

            if (response.IsSuccessStatusCode)
            {
                delivery.Status = "delivered";
                delivery.DeliveredAt = _timeProvider.GetUtcNow();
                delivery.LastError = null;
                sub.FailureCount = 0;
                sub.LastDeliveredAt = delivery.DeliveredAt;
            }
            else
            {
                delivery.Status = "dead_letter";
                delivery.LastError = $"Replay failed: HTTP {(int)response.StatusCode}: {response.ReasonPhrase}";
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            delivery.DurationMs = sw.ElapsedMilliseconds;
            delivery.Status = "dead_letter";
            delivery.LastError = $"Replay exception: {ex.Message}";
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToDeliveryDto(delivery);
    }

    public async Task<int> ProcessPendingRetriesAsync(CancellationToken ct = default)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AchillesDbContext>();
            var now = _timeProvider.GetUtcNow();

            var pendingDeliveries = await db.WebhookDeliveries
                .Include(d => d.Subscription)
                .Where(d => d.Status == "failed" && d.NextAttemptAt != null && d.NextAttemptAt <= now && d.Attempts < WebhookBackoffHelper.MaxAttempts)
                .OrderBy(d => d.NextAttemptAt)
                .Take(50)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            if (pendingDeliveries.Count == 0) return 0;

            int processed = 0;
            foreach (var delivery in pendingDeliveries)
            {
                var sub = delivery.Subscription;
                if (sub is null || !sub.IsActive)
                {
                    delivery.Status = "dead_letter";
                    delivery.NextAttemptAt = null;
                    delivery.LastError = "Subscription inactive or missing.";
                    processed++;
                    continue;
                }

                if (!WebhookSecurityValidator.IsSafeWebhookUrl(sub.Url, _isDevelopment, _allowLocalWebhooks))
                {
                    delivery.Status = "dead_letter";
                    delivery.NextAttemptAt = null;
                    delivery.LastError = "SSRF safety check failed for destination URL.";
                    processed++;
                    continue;
                }

                delivery.Attempts++;
                var attemptTime = _timeProvider.GetUtcNow();
                long unixSeconds = attemptTime.ToUnixTimeSeconds();
                string signature = WebhookSecurity.ComputeSignature(sub.Secret, unixSeconds, delivery.PayloadJson);
                string sigHeader = WebhookSecurity.BuildSignatureHeader(sub.Secret, unixSeconds, delivery.PayloadJson);

                var sw = Stopwatch.StartNew();
                try
                {
                    using var request = new HttpRequestMessage(HttpMethod.Post, sub.Url);
                    request.Headers.Add("User-Agent", "Symbolon-Webhook/1.0");
                    request.Headers.Add("X-Symbolon-Event", delivery.EventType);
                    request.Headers.Add("X-Symbolon-Delivery", delivery.Id);
                    request.Headers.Add("X-Symbolon-Attempt", delivery.Attempts.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    request.Headers.Add("X-Symbolon-Timestamp", unixSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    request.Headers.Add("X-Symbolon-Signature", sigHeader);
                    request.Headers.Add("X-Symbolon-Signature-256", $"sha256={signature}");
                    request.Content = new StringContent(delivery.PayloadJson, Encoding.UTF8, "application/json");

                    using var response = await _httpClient.SendAsync(request, ct).ConfigureAwait(false);
                    sw.Stop();
                    delivery.DurationMs = sw.ElapsedMilliseconds;
                    delivery.StatusCode = (int)response.StatusCode;

                    if (response.IsSuccessStatusCode)
                    {
                        delivery.Status = "delivered";
                        delivery.DeliveredAt = attemptTime;
                        delivery.LastError = null;
                        delivery.NextAttemptAt = null;
                        sub.FailureCount = 0;
                        sub.LastDeliveredAt = delivery.DeliveredAt;
                    }
                    else
                    {
                        delivery.LastError = $"HTTP {(int)response.StatusCode}: {response.ReasonPhrase}";
                        if (delivery.Attempts >= WebhookBackoffHelper.MaxAttempts)
                        {
                            delivery.Status = "dead_letter";
                            delivery.NextAttemptAt = null;
                        }
                        else
                        {
                            delivery.Status = "failed";
                            delivery.NextAttemptAt = attemptTime + WebhookBackoffHelper.CalculateBackoff(delivery.Attempts);
                        }
                        sub.FailureCount++;
                        if (sub.FailureCount >= 10) sub.IsActive = false;
                    }
                }
                catch (Exception ex)
                {
                    sw.Stop();
                    delivery.DurationMs = sw.ElapsedMilliseconds;
                    delivery.LastError = ex.Message;
                    if (delivery.Attempts >= WebhookBackoffHelper.MaxAttempts)
                    {
                        delivery.Status = "dead_letter";
                        delivery.NextAttemptAt = null;
                    }
                    else
                    {
                        delivery.Status = "failed";
                        delivery.NextAttemptAt = attemptTime + WebhookBackoffHelper.CalculateBackoff(delivery.Attempts);
                    }
                    sub.FailureCount++;
                    if (sub.FailureCount >= 10) sub.IsActive = false;
                }

                processed++;
            }

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return processed;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing pending webhook retries.");
            return 0;
        }
    }

    public static string ComputeSignature(string secret, long timestamp, string payloadJson)
    {
        return WebhookSecurity.ComputeSignature(secret, timestamp, payloadJson);
    }

    private static WebhookDeliveryDto ToDeliveryDto(WebhookDeliveryEntity d)
    {
        return new WebhookDeliveryDto(
            d.Id,
            d.SubscriptionId,
            d.EventType,
            d.Status,
            d.StatusCode,
            d.Attempts,
            d.DeliveredAt,
            d.LastError,
            d.CreatedAt,
            d.DurationMs);
    }
}
#pragma warning restore CA1031
