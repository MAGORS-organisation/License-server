using System.Globalization;
using System.Text.Json;

namespace Achilles.Domain.Webhooks;

public static class WebhookAdapters
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    public static string FormatPayload(string? format, WebhookEvent evt, string? subscriptionName = null)
    {
        ArgumentNullException.ThrowIfNull(evt);

        string fmt = (format ?? "json").Trim().ToUpperInvariant();
        return fmt switch
        {
            "SLACK" => FormatSlack(evt, subscriptionName),
            "TEAMS" => FormatTeams(evt, subscriptionName),
            _ => FormatStandardJson(evt)
        };
    }

    public static string FormatStandardJson(WebhookEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);

        var payload = new SymbolonWebhookPayload(
            evt.Id,
            evt.EventType,
            evt.TenantId,
            evt.Timestamp,
            evt.Data,
            evt.SubjectId);

        return JsonSerializer.Serialize(payload, JsonOptions);
    }

    public static string FormatSlack(WebhookEvent evt, string? subscriptionName = null)
    {
        ArgumentNullException.ThrowIfNull(evt);

        string color = GetEventColorHex(evt.EventType);
        string eventTitle = GetHumanReadableTitle(evt.EventType);
        string details = evt.Data is string str ? str : JsonSerializer.Serialize(evt.Data, JsonOptions);

        var slackPayload = new
        {
            text = $"[{evt.TenantId}] {eventTitle}",
            attachments = new[]
            {
                new
                {
                    color,
                    title = $"🔔 {eventTitle}",
                    text = details.Length > 800 ? details[..800] + "..." : details,
                    fields = new[]
                    {
                        new { title = "Event Type", value = evt.EventType, @short = true },
                        new { title = "Tenant", value = evt.TenantId, @short = true },
                        new { title = "Event ID", value = evt.Id, @short = true },
                        new { title = "Timestamp", value = evt.Timestamp.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture), @short = true }
                    },
                    footer = subscriptionName ?? "Symbolon License Server",
                    ts = evt.Timestamp.ToUnixTimeSeconds()
                }
            }
        };

        return JsonSerializer.Serialize(slackPayload, JsonOptions);
    }

    public static string FormatTeams(WebhookEvent evt, string? subscriptionName = null)
    {
        ArgumentNullException.ThrowIfNull(evt);

        string color = GetEventColorHex(evt.EventType).TrimStart('#');
        string eventTitle = GetHumanReadableTitle(evt.EventType);
        string details = evt.Data is string str ? str : JsonSerializer.Serialize(evt.Data, JsonOptions);

        var teamsPayload = new
        {
            @type = "MessageCard",
            @context = "http://schema.org/extensions",
            themeColor = color,
            summary = $"Symbolon Alert: {eventTitle}",
            title = $"Symbolon — {eventTitle}",
            sections = new[]
            {
                new
                {
                    activityTitle = $"{eventTitle} ({evt.EventType})",
                    activitySubtitle = $"Tenant: {evt.TenantId} | {subscriptionName ?? "Notification Hub"}",
                    text = details.Length > 800 ? details[..800] + "..." : details,
                    facts = new[]
                    {
                        new { name = "Event ID", value = evt.Id },
                        new { name = "Timestamp", value = evt.Timestamp.ToString("yyyy-MM-dd HH:mm:ss 'UTC'", CultureInfo.InvariantCulture) },
                        new { name = "Subject", value = evt.SubjectId ?? "N/A" }
                    }
                }
            }
        };

        return JsonSerializer.Serialize(teamsPayload, JsonOptions);
    }

    private static string GetEventColorHex(string eventType)
    {
        return eventType.ToUpperInvariant() switch
        {
            "LEASE.DENIED" => "#E01E5A",       // Red alert (upsell / license exhaustion)
            "FRAUD.DETECTED" => "#E01E5A",     // Red alert (security violation)
            "LICENSE.EXPIRED" => "#E01E5A",    // Red alert (expired)
            "CLUSTER.DESYNC" => "#E01E5A",     // Red alert (split brain)
            "TOKEN.THRESHOLD_LOW" => "#ECB22E",// Yellow warning (wallet low)
            "LICENSE.EXPIRING_SOON" => "#ECB22E", // Yellow warning
            "LICENSE.GRACE_ENTERED" => "#ECB22E", // Yellow warning
            "LICENSE.CREATED" => "#2EB67D",    // Green success
            "LEASE.BORROWED" => "#4A154B",     // Purple info
            "LEASE.RETURNED" => "#2EB67D",     // Green success
            _ => "#1D9BD1"                     // Blue default
        };
    }

    private static string GetHumanReadableTitle(string eventType)
    {
        return eventType.ToUpperInvariant() switch
        {
            "LEASE.DENIED" => "Kapacita licencií vyčerpaná (Seat Denied / Upsell)",
            "FRAUD.DETECTED" => "Bezpečnostný incident (Fraud / Impossible Travel)",
            "TOKEN.THRESHOLD_LOW" => "Nízky stav kreditovej peňaženky",
            "LICENSE.EXPIRING_SOON" => "Blížiaca sa expirácia licencie",
            "LICENSE.EXPIRED" => "Licencia expirovala",
            "LICENSE.GRACE_ENTERED" => "Vstup do ochrannej lehoty (Grace Period)",
            "LICENSE.CREATED" => "Nová licencia vytvorená",
            "LEASE.BORROWED" => "Offline výpožička sedadla (Borrow)",
            "LEASE.RETURNED" => "Vrátenie výpožičky sedadla",
            "CLUSTER.DESYNC" => "Klastrová desynchronizácia",
            "TEST.PING" => "Testovací webhook ping",
            _ => $"Udalosť: {eventType}"
        };
    }
}
