using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Achilles.Domain.Billing;

public sealed class LemonSqueezyBillingProcessor : IBillingWebhookProcessor
{
    public BillingProvider Provider => BillingProvider.LemonSqueezy;

    public bool VerifySignature(string secret, string? headerValue, string rawPayload, DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(headerValue) || rawPayload is null)
        {
            return false;
        }

        byte[] keyBytes = Encoding.UTF8.GetBytes(secret);
        byte[] dataBytes = Encoding.UTF8.GetBytes(rawPayload);
        byte[] expectedHash = HMACSHA256.HashData(keyBytes, dataBytes);
        string expectedHex = Convert.ToHexString(expectedHash);

        byte[] expectedBytes = Encoding.UTF8.GetBytes(expectedHex);
        byte[] actualBytes = Encoding.UTF8.GetBytes(headerValue.Trim().ToUpperInvariant());

        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    public BillingWebhookEvent ParseEvent(string rawPayload)
    {
        ArgumentNullException.ThrowIfNull(rawPayload);

        using var doc = JsonDocument.Parse(rawPayload);
        var root = doc.RootElement;

        string eventName = "unknown";
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (root.TryGetProperty("meta", out var metaProp))
        {
            if (metaProp.TryGetProperty("event_name", out var evProp))
            {
                eventName = evProp.GetString() ?? "unknown";
            }

            if (metaProp.TryGetProperty("custom_data", out var cdProp) && cdProp.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in cdProp.EnumerateObject())
                {
                    metadata[prop.Name] = prop.Value.GetString() ?? string.Empty;
                }
            }
        }

        string eventId = root.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? $"ls_{Guid.NewGuid():N}" : $"ls_{Guid.NewGuid():N}";

        var dataProp = root.TryGetProperty("data", out var dProp) ? dProp : root;
        string? subId = null;
        if (dataProp.TryGetProperty("id", out var dataIdProp))
        {
            subId = dataIdProp.GetString();
        }

        var attr = dataProp.TryGetProperty("attributes", out var attrProp) ? attrProp : dataProp;

        string customerId = string.Empty;
        if (attr.TryGetProperty("customer_id", out var cidProp))
        {
            customerId = cidProp.ValueKind == JsonValueKind.Number ? cidProp.GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture) : cidProp.GetString() ?? string.Empty;
        }

        string customerEmail = string.Empty;
        string? customerName = null;
        if (attr.TryGetProperty("user_email", out var emailProp)) customerEmail = emailProp.GetString() ?? string.Empty;
        if (attr.TryGetProperty("user_name", out var nameProp)) customerName = nameProp.GetString();

        string? status = null;
        if (attr.TryGetProperty("status", out var statusProp)) status = statusProp.GetString();

        DateTimeOffset? expiresAt = null;
        if (attr.TryGetProperty("renews_at", out var renewsProp) && renewsProp.TryGetDateTimeOffset(out var renewsDto))
        {
            expiresAt = renewsDto;
        }
        else if (attr.TryGetProperty("ends_at", out var endsProp) && endsProp.TryGetDateTimeOffset(out var endsDto))
        {
            expiresAt = endsDto;
        }

        int seats = 1;
        if (metadata.TryGetValue("seats", out var seatsStr) && int.TryParse(seatsStr, out int s))
        {
            seats = s;
        }

        string? productCode = null;
        if (metadata.TryGetValue("product", out var p1)) productCode = p1;
        else if (metadata.TryGetValue("productCode", out var p2)) productCode = p2;

        string? planCode = null;
        if (attr.TryGetProperty("variant_id", out var varIdProp))
        {
            planCode = varIdProp.ToString();
        }

        BillingAction action = eventName switch
        {
            "order_created" => BillingAction.Provision,
            "subscription_created" => BillingAction.Provision,
            "subscription_updated" => string.Equals(status, "past_due", StringComparison.OrdinalIgnoreCase)
                ? BillingAction.Suspend
                : BillingAction.Renew,
            "subscription_resumed" => BillingAction.Renew,
            "subscription_cancelled" or "subscription_expired" => BillingAction.Revoke,
            "subscription_payment_failed" => BillingAction.Suspend,
            "subscription_payment_success" => BillingAction.Renew,
            _ => BillingAction.Ignored
        };

        if (string.IsNullOrWhiteSpace(customerId))
        {
            customerId = customerEmail.Length > 0 ? customerEmail : $"ls_cus_{Guid.NewGuid():N}";
        }

        return new BillingWebhookEvent
        {
            Provider = BillingProvider.LemonSqueezy,
            EventId = eventId,
            EventType = eventName,
            Action = action,
            CustomerId = customerId,
            CustomerEmail = customerEmail,
            CustomerName = customerName,
            SubscriptionId = subId,
            ProductCode = productCode,
            PlanCode = planCode,
            Seats = Math.Max(1, seats),
            ExpiresAt = expiresAt,
            Status = status,
            Metadata = metadata
        };
    }
}
