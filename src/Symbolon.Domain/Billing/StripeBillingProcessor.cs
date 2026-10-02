using System.Text.Json;
using Symbolon.Domain.Webhooks;

namespace Symbolon.Domain.Billing;

public sealed class StripeBillingProcessor : IBillingWebhookProcessor
{
    public BillingProvider Provider => BillingProvider.Stripe;

    public bool VerifySignature(string secret, string? headerValue, string rawPayload, DateTimeOffset? now = null)
    {
        return WebhookSecurity.VerifySignatureHeader(secret, headerValue, rawPayload, tolerance: TimeSpan.FromMinutes(5), now: now);
    }

    public BillingWebhookEvent ParseEvent(string rawPayload)
    {
        ArgumentNullException.ThrowIfNull(rawPayload);

        using var doc = JsonDocument.Parse(rawPayload);
        var root = doc.RootElement;

        string eventId = root.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? $"evt_{Guid.NewGuid():N}" : $"evt_{Guid.NewGuid():N}";
        string eventType = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() ?? "unknown" : "unknown";

        var dataObj = root.TryGetProperty("data", out var dataProp) && dataProp.TryGetProperty("object", out var objProp)
            ? objProp
            : root;

        string customerId = string.Empty;
        if (dataObj.TryGetProperty("customer", out var cusProp))
        {
            customerId = cusProp.ValueKind == JsonValueKind.String ? cusProp.GetString() ?? string.Empty : string.Empty;
        }

        string customerEmail = string.Empty;
        string? customerName = null;
        if (dataObj.TryGetProperty("customer_details", out var cusDetails))
        {
            if (cusDetails.TryGetProperty("email", out var emailProp)) customerEmail = emailProp.GetString() ?? string.Empty;
            if (cusDetails.TryGetProperty("name", out var nameProp)) customerName = nameProp.GetString();
        }
        else if (dataObj.TryGetProperty("customer_email", out var emailProp))
        {
            customerEmail = emailProp.GetString() ?? string.Empty;
        }

        string? subscriptionId = null;
        if (dataObj.TryGetProperty("subscription", out var subProp))
        {
            subscriptionId = subProp.ValueKind == JsonValueKind.String ? subProp.GetString() : null;
        }
        else if (dataObj.TryGetProperty("id", out var subIdProp) && eventType.StartsWith("customer.subscription.", StringComparison.OrdinalIgnoreCase))
        {
            subscriptionId = subIdProp.GetString();
        }

        int seats = 1;
        DateTimeOffset? expiresAt = null;
        string? status = null;
        string? productCode = null;
        string? planCode = null;
        var metadata = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (dataObj.TryGetProperty("metadata", out var metaProp) && metaProp.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in metaProp.EnumerateObject())
            {
                string val = prop.Value.GetString() ?? string.Empty;
                metadata[prop.Name] = val;
                if (prop.Name.Equals("seats", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, out int s)) seats = s;
                if (prop.Name.Equals("product", StringComparison.OrdinalIgnoreCase) || prop.Name.Equals("productCode", StringComparison.OrdinalIgnoreCase)) productCode = val;
                if (prop.Name.Equals("plan", StringComparison.OrdinalIgnoreCase) || prop.Name.Equals("planCode", StringComparison.OrdinalIgnoreCase)) planCode = val;
            }
        }

        if (dataObj.TryGetProperty("current_period_end", out var periodEndProp) && periodEndProp.TryGetInt64(out long endSec))
        {
            expiresAt = DateTimeOffset.FromUnixTimeSeconds(endSec);
        }

        if (dataObj.TryGetProperty("status", out var statusProp))
        {
            status = statusProp.GetString();
        }

        if (dataObj.TryGetProperty("items", out var itemsProp) &&
            itemsProp.TryGetProperty("data", out var itemDataProp) &&
            itemDataProp.ValueKind == JsonValueKind.Array &&
            itemDataProp.GetArrayLength() > 0)
        {
            var firstItem = itemDataProp[0];
            if (firstItem.TryGetProperty("quantity", out var qtyProp) && qtyProp.TryGetInt32(out int q))
            {
                seats = q;
            }
            if (firstItem.TryGetProperty("price", out var priceProp) && priceProp.TryGetProperty("id", out var priceIdProp))
            {
                planCode ??= priceIdProp.GetString();
            }
        }

        BillingAction action = eventType switch
        {
            "checkout.session.completed" => BillingAction.Provision,
            "customer.subscription.created" => BillingAction.Provision,
            "customer.subscription.updated" => string.Equals(status, "past_due", StringComparison.OrdinalIgnoreCase)
                ? BillingAction.Suspend
                : BillingAction.Renew,
            "customer.subscription.deleted" => BillingAction.Revoke,
            "invoice.payment_succeeded" => BillingAction.Renew,
            "invoice.payment_failed" => BillingAction.Suspend,
            _ => BillingAction.Ignored
        };

        if (string.IsNullOrWhiteSpace(customerId))
        {
            customerId = customerEmail.Length > 0 ? customerEmail : $"cus_{Guid.NewGuid():N}";
        }

        return new BillingWebhookEvent
        {
            Provider = BillingProvider.Stripe,
            EventId = eventId,
            EventType = eventType,
            Action = action,
            CustomerId = customerId,
            CustomerEmail = customerEmail,
            CustomerName = customerName,
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
