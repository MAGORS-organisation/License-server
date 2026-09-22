namespace Symbolon.Domain.Webhooks;

#pragma warning disable CA1054 // URI-like parameters should not be strings (Store interface)
public interface IWebhookStore
{
    Task<IReadOnlyList<WebhookSubscriptionModel>> GetSubscriptionsAsync(string? tenantId, string? eventType = null, CancellationToken ct = default);
    Task<WebhookSubscriptionModel?> GetSubscriptionByIdAsync(string id, CancellationToken ct = default);
    Task<WebhookSubscriptionModel> CreateSubscriptionAsync(string tenantId, string name, string url, string secret, string format, IReadOnlyList<string> events, CancellationToken ct = default);
    Task<bool> DeleteSubscriptionAsync(string id, string? tenantId = null, CancellationToken ct = default);
    Task LogDeliveryAsync(WebhookDeliveryModel delivery, CancellationToken ct = default);
    Task<IReadOnlyList<WebhookDeliveryModel>> GetDeliveriesAsync(string? subscriptionId = null, string? status = null, int limit = 50, CancellationToken ct = default);
    Task<WebhookDeliveryModel?> GetDeliveryByIdAsync(string id, CancellationToken ct = default);
    Task UpdateSubscriptionFailureAsync(string subscriptionId, bool success, CancellationToken ct = default);
}
#pragma warning restore CA1054
