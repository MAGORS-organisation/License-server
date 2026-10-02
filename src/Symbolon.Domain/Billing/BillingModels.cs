using System.Text.Json.Serialization;

namespace Symbolon.Domain.Billing;

public enum BillingProvider
{
    Stripe,
    LemonSqueezy,
    Paddle
}

public enum BillingAction
{
    Provision,
    Renew,
    UpdateSeats,
    Suspend,
    Revoke,
    Ignored
}

public sealed record BillingWebhookEvent
{
    public required BillingProvider Provider { get; init; }
    public required string EventId { get; init; }
    public required string EventType { get; init; }
    public required BillingAction Action { get; init; }
    public required string CustomerId { get; init; }
    public required string CustomerEmail { get; init; }
    public string? CustomerName { get; init; }
    public string? SubscriptionId { get; init; }
    public string? ProductCode { get; init; }
    public string? PlanCode { get; init; }
    public int Seats { get; init; } = 1;
    public DateTimeOffset? ExpiresAt { get; init; }
    public string? Status { get; init; }
    public Dictionary<string, string> Metadata { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record BillingProcessResult(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("licenseId")] string? LicenseId,
    [property: JsonPropertyName("licenseKey")] string? LicenseKey,
    [property: JsonPropertyName("message")] string Message,
    [property: JsonPropertyName("customerId")] string? CustomerId = null,
    [property: JsonPropertyName("subscriptionId")] string? SubscriptionId = null);
