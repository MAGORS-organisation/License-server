using System.Collections.Concurrent;
using Achilles.Domain;
using Achilles.Protocol;

namespace Achilles.Relay;

internal sealed class RelayQueueTicket
{
    public required string Ticket { get; init; }
    public required string LicenseId { get; init; }
    public required string Fingerprint { get; init; }
    public string? MachineId { get; init; }
    public int Quantity { get; init; }
    public int Priority { get; init; }
    public IReadOnlyList<string>? Features { get; init; }
    public string Status { get; set; } = "waiting";
    public string? PromotedLeaseId { get; set; }
    public string? PromotedToken { get; set; }
    public int? PromotedSeatNo { get; set; }
    public DateTimeOffset? PromotedExpiresAt { get; set; }
    public DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

internal sealed class RelayQueueManager
{
    private readonly ConcurrentDictionary<string, RelayQueueTicket> _tickets = new(StringComparer.Ordinal);
    private readonly LeaseEngine _engine;
    private readonly TimeProvider _timeProvider;

    public RelayQueueManager(LeaseEngine engine, TimeProvider timeProvider)
    {
        _engine = engine;
        _timeProvider = timeProvider;
    }

    public RelayQueueTicket Enqueue(
        string licenseId,
        string fingerprint,
        string? machineId,
        int quantity,
        IReadOnlyList<string>? features,
        int priority,
        TimeSpan ttl)
    {
        var now = _timeProvider.GetUtcNow();
        var ticket = new RelayQueueTicket
        {
            Ticket = $"q_{Guid.NewGuid():N}",
            LicenseId = licenseId,
            Fingerprint = fingerprint,
            MachineId = machineId,
            Quantity = quantity,
            Priority = priority,
            Features = features,
            ExpiresAt = now.Add(ttl),
            CreatedAt = now
        };

        _tickets[ticket.Ticket] = ticket;
        return ticket;
    }

    public int GetPosition(RelayQueueTicket ticket)
    {
        return _tickets.Values
            .Count(t => t.LicenseId == ticket.LicenseId && t.Status == "waiting" &&
                        (t.Priority > ticket.Priority || (t.Priority == ticket.Priority && t.CreatedAt < ticket.CreatedAt))) + 1;
    }

    public async Task<QueueStatusResponseDto?> GetStatusAsync(string ticket, CancellationToken ct = default)
    {
        if (!_tickets.TryGetValue(ticket, out var item))
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow();
        if (item.Status == "waiting" && item.ExpiresAt < now)
        {
            item.Status = "expired";
        }

        if (item.Status == "waiting")
        {
            await TryPromoteNextAsync(item.LicenseId, ct).ConfigureAwait(false);
        }

        if (item.Status == "ready")
        {
            return new QueueStatusResponseDto
            {
                Ticket = item.Ticket,
                Status = "ready",
                Position = 0,
                Priority = item.Priority,
                LeaseId = item.PromotedLeaseId,
                Token = item.PromotedToken,
                Seat = item.PromotedSeatNo,
                ExpiresAt = item.PromotedExpiresAt
            };
        }

        if (item.Status == "waiting")
        {
            int position = GetPosition(item);
            return new QueueStatusResponseDto
            {
                Ticket = item.Ticket,
                Status = "waiting",
                Position = position,
                Priority = item.Priority,
                EstimatedWait = "PT2M",
                RetryAfterSeconds = 3
            };
        }

        return new QueueStatusResponseDto
        {
            Ticket = item.Ticket,
            Status = item.Status,
            Priority = item.Priority
        };
    }

    public bool Cancel(string ticket)
    {
        if (_tickets.TryGetValue(ticket, out var item) && item.Status == "waiting")
        {
            item.Status = "cancelled";
            return true;
        }
        return false;
    }

    public async Task<int> TryPromoteNextAsync(string licenseId, CancellationToken ct = default)
    {
        var now = _timeProvider.GetUtcNow();
        var candidates = _tickets.Values
            .Where(t => t.LicenseId == licenseId && t.Status == "waiting" && t.ExpiresAt > now)
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.CreatedAt)
            .ToList();

        int promotedCount = 0;
        foreach (var ticket in candidates)
        {
            var cmd = new CheckoutCommand(
                ticket.LicenseId,
                ticket.Fingerprint,
                ticket.MachineId,
                ticket.Quantity,
                ticket.Features,
                ticket.Ticket,
                AllowQueue: false,
                Ttl: TimeSpan.FromMinutes(10));

            var result = await _engine.CheckoutAsync(cmd, ct).ConfigureAwait(false);
            if (result.IsSuccess && result.Allocations is { Count: > 0 } && result.Tokens is { Count: > 0 })
            {
                var alloc = result.Allocations[0];
                ticket.Status = "ready";
                ticket.PromotedLeaseId = alloc.LeaseId;
                ticket.PromotedToken = result.Tokens[0];
                ticket.PromotedSeatNo = alloc.SeatNo;
                ticket.PromotedExpiresAt = alloc.ExpiresAt;
                promotedCount++;
            }
            else
            {
                break;
            }
        }

        return promotedCount;
    }
}
