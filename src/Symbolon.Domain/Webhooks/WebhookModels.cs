using System.Text.Json.Serialization;

namespace Symbolon.Domain.Webhooks;

public static class WebhookEventTypes
{
    public const string LicenseCreated = "license.created";
    public const string LicenseExpiringSoon = "license.expiring_soon";
    public const string LicenseExpired = "license.expired";
    public const string LicenseGraceEntered = "license.grace_entered";
    public const string LeaseDenied = "lease.denied";
    public const string LeaseBorrowed = "lease.borrowed";
    public const string LeaseReturned = "lease.returned";
    public const string FraudDetected = "fraud.detected";
    public const string TokenThresholdLow = "token.threshold_low";
    public const string ClusterDesync = "cluster.desync";
    public const string TestPing = "test.ping";
    public const string Wildcard = "*";

    public static readonly IReadOnlyList<string> AllKnown =
    [
        LicenseCreated,
        LicenseExpiringSoon,
        LicenseExpired,
        LicenseGraceEntered,
        LeaseDenied,
        LeaseBorrowed,
        LeaseReturned,
        FraudDetected,
        TokenThresholdLow,
        ClusterDesync,
        TestPing
    ];
}

public sealed record WebhookEvent(
    string Id,
    string EventType,
    string TenantId,
    DateTimeOffset Timestamp,
    object Data,
    string? SubjectId = null);

public sealed record SymbolonWebhookPayload(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("tenantId")] string TenantId,
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp,
    [property: JsonPropertyName("data")] object Data,
    [property: JsonPropertyName("subjectId")] string? SubjectId = null);

#pragma warning disable CA1054, CA1056 // URI-like properties should not be strings (Domain model mapping)
public sealed record WebhookSubscriptionModel(
    string Id,
    string TenantId,
    string Name,
    string Url,
    string Secret,
    string Format,
    IReadOnlyList<string> Events,
    bool IsActive,
    int FailureCount,
    DateTimeOffset? LastDeliveredAt,
    DateTimeOffset CreatedAt);
#pragma warning restore CA1054, CA1056

public sealed record WebhookDeliveryModel(
    string Id,
    string SubscriptionId,
    string EventType,
    string PayloadJson,
    string Status,
    int? StatusCode,
    long DurationMs,
    int Attempts,
    DateTimeOffset? DeliveredAt,
    string? LastError,
    DateTimeOffset CreatedAt);
