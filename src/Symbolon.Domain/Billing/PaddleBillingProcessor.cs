using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Symbolon.Domain.Billing;

public sealed class PaddleBillingProcessor : IBillingWebhookProcessor
{
    private static readonly TimeSpan DefaultTolerance = TimeSpan.FromMinutes(5);

    public BillingProvider Provider => BillingProvider.Paddle;

    public bool VerifySignature(string secret, string? headerValue, string rawPayload, DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(secret) || string.IsNullOrWhiteSpace(headerValue) || rawPayload is null)
        {
            return false;
        }

        long? timestamp = null;
        string? signatureHex = null;

        var parts = headerValue.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var part in parts)
        {
            var kv = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (kv.Length != 2) continue;

            if (string.Equals(kv[0], "ts", StringComparison.OrdinalIgnoreCase) &&
                long.TryParse(kv[1], System.Globalization.CultureInfo.InvariantCulture, out long parsedTs))
            {
                timestamp = parsedTs;
            }
            else if (string.Equals(kv[0], "h1", StringComparison.OrdinalIgnoreCase))
            {
                signatureHex = kv[1];
            }
        }

        if (timestamp is null || string.IsNullOrWhiteSpace(signatureHex))
        {
            return false;
        }

        var currentTime = now ?? DateTimeOffset.UtcNow;
        var headerTime = DateTimeOffset.FromUnixTimeSeconds(timestamp.Value);
        if (currentTime - headerTime > DefaultTolerance || headerTime - currentTime > DefaultTolerance)
        {
            return false;
        }

        string stringToSign = $"{timestamp.Value}:{rawPayload}";
        byte[] keyBytes = Encoding.UTF8.GetBytes(secret);
        byte[] dataBytes = Encoding.UTF8.GetBytes(stringToSign);
        byte[] expectedHash = HMACSHA256.HashData(keyBytes, dataBytes);
        string expectedHex = Convert.ToHexString(expectedHash);

        byte[] expectedBytes = Encoding.UTF8.GetBytes(expectedHex);
        byte[] actualBytes = Encoding.UTF8.GetBytes(signatureHex.Trim().ToUpperInvariant());

        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    public BillingWebhookEvent ParseEvent(string rawPayload)
    {
        ArgumentNullException.ThrowIfNull(rawPayload);

        using var doc = JsonDocument.Parse(rawPayload);
        var root = doc.RootElement;

        string eventId = root.TryGetProperty("event_id", out var eidProp) ? eidProp.GetString() ?? $"pad_{Guid.NewGuid():N}" : $"pad_{Guid.NewGuid():N}";
        string eventType = root.TryGetProperty("event_type", out var typeProp) ? typeProp.GetString() ?? "unknown" : "unknown";

        var dataObj = root.TryGetProperty("data", out var dProp) ? dProp : root;

        string customerId = string.Empty;
        if (dataObj.TryGetProperty("customer_id", out var cidProp))
        {
            customerId = cidProp.GetString() ?? string.Empty;
        }

        string? subscriptionId = null;
        if (dataObj.TryGetProperty("id", out var idProp) && eventType.StartsWith("subscription.", StringComparison.OrdinalIgnoreCase))
        {
            subscriptionId = idProp.GetString();
        }
        else if (dataObj.TryGetProperty("subscription_id", out var subProp))
        {
            subscriptionId = subProp.GetString();
        }

        string? status = null;
        if (dataObj.TryGetProperty("status", out var statusProp)) status = statusProp.GetString();

        DateTimeOffset? expiresAt = null;
        if (dataObj.TryGetProperty("current_billing_period", out var cbpProp) &&
            cbpProp.TryGetProperty("ends_at", out var endsProp) &&
            endsProp.TryGetDateTimeOffset(out var endsDto))
        {
            expiresAt = endsDto;
        }
        else if (dataObj.TryGetProperty("next_billed_at", out var nextProp) &&
                 nextProp.TryGetDateTimeOffset(out var nextDto))
        {
            expiresAt = nextDto;
        }

        int seats = 1;
        string? planCode = null;
        if (dataObj.TryGetProperty("items", out var itemsProp) &&
            itemsProp.ValueKind == JsonValueKind.Array &&
            itemsProp.GetArrayLength() > 0)
        {
            var firstItem = itemsProp[0];
            if (firstItem.TryGetProperty("quantity", out var qtyProp) && qtyProp.TryGetInt32(out int q))
            {
                seats = q;
            }
            if (firstItem.TryGetProperty("price", out var priceProp) &&
                priceProp.TryGetProperty("id", out var priceIdProp))
            {
                planCode = priceIdProp.GetString();
            }
        }

        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (dataObj.TryGetProperty("custom_data", out var cdProp) && cdProp.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in cdProp.EnumerateObject())
            {
                metadata[prop.Name] = prop.Value.GetString() ?? string.Empty;
            }
        }

        string? productCode = null;
        if (metadata.TryGetValue("product", out var p1)) productCode = p1;
        else if (metadata.TryGetValue("productCode", out var p2)) productCode = p2;

        if (metadata.TryGetValue("seats", out var seatsStr) && int.TryParse(seatsStr, out int s))
        {
            seats = s;
        }

        BillingAction action = eventType switch
        {
            "transaction.completed" => BillingAction.Provision,
            "subscription.created" => BillingAction.Provision,
            "subscription.updated" => string.Equals(status, "past_due", StringComparison.OrdinalIgnoreCase)
                ? BillingAction.Suspend
                : BillingAction.Renew,
            "subscription.canceled" => BillingAction.Revoke,
            "subscription.past_due" => BillingAction.Suspend,
            _ => BillingAction.Ignored
        };

        string customerEmail = metadata.TryGetValue("email", out var em) ? em : string.Empty;

        return new BillingWebhookEvent
        {
            Provider = BillingProvider.Paddle,
            EventId = eventId,
            EventType = eventType,
            Action = action,
            CustomerId = customerId.Length > 0 ? customerId : $"pad_cus_{Guid.NewGuid():N}",
            CustomerEmail = customerEmail,
            SubscriptionId = subscriptionId,
            ProductCode = productCode,
            PlanCode = planCode,
            Seats = Math.Max(1, seats),
            ExpiresAt = expiresAt,
            Status = status,
            Metadata = metadata
        };
    }
}
