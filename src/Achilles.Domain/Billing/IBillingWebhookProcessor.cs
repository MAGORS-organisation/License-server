namespace Achilles.Domain.Billing;

public interface IBillingWebhookProcessor
{
    BillingProvider Provider { get; }

    bool VerifySignature(string secret, string? headerValue, string rawPayload, DateTimeOffset? now = null);

    BillingWebhookEvent ParseEvent(string rawPayload);
}
