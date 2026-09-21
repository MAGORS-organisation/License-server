namespace Symbolon.Domain;

public sealed record AuditEvent(
    string Type,
    string LicenseId,
    string? LeaseId,
    string? Fingerprint,
    DateTimeOffset Timestamp,
    string? Detail);

/// <summary>
/// Append-only audit ledger for license events.
/// </summary>
public interface IAuditLedger
{
    Task AppendAsync(AuditEvent auditEvent, CancellationToken ct = default);
}

/// <summary>
/// Simple in-memory or no-op audit ledger implementation.
/// </summary>
public sealed class InMemoryAuditLedger : IAuditLedger
{
    private readonly List<AuditEvent> _events = [];
    private readonly object _lock = new();

    public IReadOnlyList<AuditEvent> Events
    {
        get
        {
            lock (_lock)
            {
                return [.. _events];
            }
        }
    }

    public Task AppendAsync(AuditEvent auditEvent, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(auditEvent);
        lock (_lock)
        {
            _events.Add(auditEvent);
        }
        return Task.CompletedTask;
    }
}
