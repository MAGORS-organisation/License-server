using System.Collections.Concurrent;
using Symbolon.ControlPlane.Observability;
using Symbolon.ControlPlane.Webhooks;
using Symbolon.Domain;

namespace Symbolon.ControlPlane.Alerting;

public interface IAlertService
{
    Task CheckCapacityThresholdAsync(string licenseId, int activeSeats, int maxSeats, string tenantId, CancellationToken ct = default);
    Task RecordDenialSpikeAsync(string licenseId, string tenantId, string reason, CancellationToken ct = default);
    Task TriggerSecurityAlertAsync(string alertType, string tenantId, string description, CancellationToken ct = default);
}

public sealed class AlertService : IAlertService
{
    private readonly IWebhookDispatcher _webhooks;
    private readonly IAuditLedger _audit;
    private readonly SymbolonMetrics _metrics;
    private readonly TimeProvider _time;
    private readonly ILogger<AlertService> _logger;

    // Rolling window tracker for denials: licenseId -> list of timestamps
    private readonly ConcurrentDictionary<string, ConcurrentQueue<DateTimeOffset>> _denialWindows = new();
    private readonly ConcurrentDictionary<string, DateTimeOffset> _lastCapacityAlert = new();

    public AlertService(
        IWebhookDispatcher webhooks,
        IAuditLedger audit,
        SymbolonMetrics metrics,
        TimeProvider time,
        ILogger<AlertService> logger)
    {
        _webhooks = webhooks;
        _audit = audit;
        _metrics = metrics;
        _time = time;
        _logger = logger;
    }

    public async Task CheckCapacityThresholdAsync(string licenseId, int activeSeats, int maxSeats, string tenantId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(licenseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        if (maxSeats <= 0) return;

        double utilization = (double)activeSeats / maxSeats;
        if (utilization >= 0.90) // 90% or higher capacity alert
        {
            var now = _time.GetUtcNow();
            // Throttle capacity alerts to once per 10 minutes per license
            if (_lastCapacityAlert.TryGetValue(licenseId, out var lastAlert) && (now - lastAlert) < TimeSpan.FromMinutes(10))
            {
                return;
            }

            _lastCapacityAlert[licenseId] = now;
            _metrics.RecordAlert("capacity_exhausted");

            var alertPayload = new
            {
                alert = "CAPACITY_EXHAUSTED",
                licenseId,
                tenantId,
                activeSeats,
                maxSeats,
                utilizationPercent = Math.Round(utilization * 100.0, 1),
                timestamp = now
            };

            _logger.LogWarning("Capacity alert triggered for license {LicenseId}: {Active}/{Max} seats ({Percent}%)",
                licenseId, activeSeats, maxSeats, Math.Round(utilization * 100.0, 1));

            await _audit.AppendAsync(new AuditEvent(
                "alert.capacity_exhausted",
                licenseId,
                null,
                null,
                now,
                $"License reached {Math.Round(utilization * 100.0, 1)}% capacity ({activeSeats}/{maxSeats} seats)"), ct).ConfigureAwait(false);

            await _webhooks.PublishEventAsync("alert.capacity_exhausted", alertPayload, tenantId, ct).ConfigureAwait(false);
        }
    }

    public async Task RecordDenialSpikeAsync(string licenseId, string tenantId, string reason, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(licenseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var now = _time.GetUtcNow();
        var queue = _denialWindows.GetOrAdd(licenseId, _ => new ConcurrentQueue<DateTimeOffset>());
        queue.Enqueue(now);

        // Prune entries older than 1 minute
        var cutoff = now.AddMinutes(-1);
        while (queue.TryPeek(out var oldest) && oldest < cutoff)
        {
            queue.TryDequeue(out _);
        }

        if (queue.Count >= 5) // 5 denials within 1 minute = Denial Spike Alert
        {
            _metrics.RecordAlert("denial_spike");

            var alertPayload = new
            {
                alert = "DENIAL_SPIKE",
                licenseId,
                tenantId,
                denialsInWindow = queue.Count,
                windowSeconds = 60,
                lastReason = reason,
                timestamp = now
            };

            _logger.LogWarning("Denial spike alert triggered for license {LicenseId}: {Count} denials within 1 minute", licenseId, queue.Count);

            await _audit.AppendAsync(new AuditEvent(
                "alert.denial_spike",
                licenseId,
                null,
                null,
                now,
                $"Denial spike detected: {queue.Count} denials in the last minute. Reason: {reason}"), ct).ConfigureAwait(false);

            await _webhooks.PublishEventAsync("alert.denial_spike", alertPayload, tenantId, ct).ConfigureAwait(false);
        }
    }

    public async Task TriggerSecurityAlertAsync(string alertType, string tenantId, string description, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alertType);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        var now = _time.GetUtcNow();
        _metrics.RecordAlert(alertType);

        var alertPayload = new
        {
            alert = "SECURITY_ANOMALY",
            type = alertType,
            tenantId,
            description,
            timestamp = now
        };

        _logger.LogWarning("Security alert [{AlertType}] for tenant {TenantId}: {Description}", alertType, tenantId, description);

        await _audit.AppendAsync(new AuditEvent(
            $"alert.security.{alertType}",
            tenantId,
            null,
            null,
            now,
            description), ct).ConfigureAwait(false);

        await _webhooks.PublishEventAsync("alert.security", alertPayload, tenantId, ct).ConfigureAwait(false);
    }
}
