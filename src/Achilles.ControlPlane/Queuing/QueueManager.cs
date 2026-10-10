using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Achilles.Data;
using Achilles.Data.Entities;
using Achilles.Domain;
using Achilles.Protocol;

namespace Achilles.ControlPlane.Queuing;

public interface IQueueManager
{
    Task<QueueTicketEntity> EnqueueAsync(
        string licenseId,
        string fingerprint,
        string? machineId,
        string? userId,
        int quantity,
        IReadOnlyList<string>? features,
        TimeSpan ttl,
        int priority = 0,
        CancellationToken ct = default);

    Task<QueueStatusResponseDto?> GetStatusAsync(string ticket, CancellationToken ct = default);

    Task<bool> CancelAsync(string ticket, CancellationToken ct = default);

    Task<int> TryPromoteNextAsync(string licenseId, CancellationToken ct = default);

    Task<bool> PromoteTicketAsync(string ticket, CancellationToken ct = default);

    Task<List<QueueTicketItemDto>> GetTicketsAsync(string? licenseId = null, string? status = null, int limit = 50, CancellationToken ct = default);

    Task<int> SweepExpiredTicketsAsync(CancellationToken ct = default);
}

public sealed class QueueManager : IQueueManager
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<QueueManager> _logger;

    public QueueManager(
        IServiceScopeFactory scopeFactory,
        TimeProvider timeProvider,
        ILogger<QueueManager> logger)
    {
        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task<QueueTicketEntity> EnqueueAsync(
        string licenseId,
        string fingerprint,
        string? machineId,
        string? userId,
        int quantity,
        IReadOnlyList<string>? features,
        TimeSpan ttl,
        int priority = 0,
        CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AchillesDbContext>();

        var now = _timeProvider.GetUtcNow();
        var expiresAt = now.Add(ttl);
        string ticketId = $"q_{Guid.NewGuid():N}";

        int resolvedPriority = priority;
        if (resolvedPriority == 0 && !string.IsNullOrWhiteSpace(userId))
        {
            var user = await db.LicenseUsers.AsNoTracking().FirstOrDefaultAsync(u => u.LicenseId == licenseId && u.UserId == userId, ct).ConfigureAwait(false);
            if (user?.GroupName is { } g && (g.Contains("power", StringComparison.OrdinalIgnoreCase) || g.Contains("vip", StringComparison.OrdinalIgnoreCase) || g.Contains("admin", StringComparison.OrdinalIgnoreCase)))
            {
                resolvedPriority = 10;
            }
        }

        var ticket = new QueueTicketEntity
        {
            Ticket = ticketId,
            LicenseId = licenseId,
            Fingerprint = fingerprint,
            MachineId = machineId,
            UserId = userId,
            Quantity = quantity,
            Priority = resolvedPriority,
            FeaturesJson = JsonSerializer.Serialize(features ?? []),
            Status = "waiting",
            ExpiresAt = expiresAt,
            CreatedAt = now
        };

        db.QueueTickets.Add(ticket);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Client enqueued ticket {Ticket} for license {LicenseId} (Priority: {Priority})", ticketId, licenseId, resolvedPriority);
        }
        return ticket;
    }

    public async Task<QueueStatusResponseDto?> GetStatusAsync(string ticket, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AchillesDbContext>();

        var entity = await db.QueueTickets.FirstOrDefaultAsync(q => q.Ticket == ticket, ct).ConfigureAwait(false);
        if (entity is null)
        {
            return null;
        }

        var now = _timeProvider.GetUtcNow();
        if (entity.Status == "waiting" && entity.ExpiresAt < now)
        {
            entity.Status = "expired";
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        if (entity.Status == "ready")
        {
            return new QueueStatusResponseDto
            {
                Ticket = entity.Ticket,
                Status = "ready",
                Position = 0,
                Priority = entity.Priority,
                LeaseId = entity.PromotedLeaseId,
                Token = entity.PromotedToken,
                Seat = entity.PromotedSeatNo,
                ExpiresAt = entity.PromotedExpiresAt
            };
        }

        if (entity.Status == "waiting")
        {
            int position = await db.QueueTickets
                .Where(q => q.LicenseId == entity.LicenseId && q.Status == "waiting" &&
                            (q.Priority > entity.Priority || (q.Priority == entity.Priority && q.CreatedAt < entity.CreatedAt)))
                .CountAsync(ct)
                .ConfigureAwait(false) + 1;

            var nextExpiring = await db.Seats
                .Where(s => s.LicenseId == entity.LicenseId && s.LeaseId != null && s.ExpiresAt > now)
                .OrderBy(s => s.ExpiresAt)
                .Select(s => s.ExpiresAt)
                .Take(Math.Max(1, position))
                .ToListAsync(ct)
                .ConfigureAwait(false);

            TimeSpan waitEstimate = TimeSpan.FromMinutes(2);
            if (nextExpiring.Count > 0)
            {
                var targetExp = nextExpiring[^1];
                if (targetExp.HasValue && targetExp.Value > now)
                {
                    waitEstimate = targetExp.Value - now;
                }
            }

            int retryAfterSec = Math.Clamp((int)(waitEstimate.TotalSeconds / 4), 2, 10);
            string waitIso = System.Xml.XmlConvert.ToString(waitEstimate);

            return new QueueStatusResponseDto
            {
                Ticket = entity.Ticket,
                Status = "waiting",
                Position = position,
                Priority = entity.Priority,
                EstimatedWait = waitIso,
                RetryAfterSeconds = retryAfterSec
            };
        }

        return new QueueStatusResponseDto
        {
            Ticket = entity.Ticket,
            Status = entity.Status,
            Priority = entity.Priority
        };
    }

    public async Task<bool> CancelAsync(string ticket, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AchillesDbContext>();

        var entity = await db.QueueTickets.FirstOrDefaultAsync(q => q.Ticket == ticket, ct).ConfigureAwait(false);
        if (entity is null || entity.Status != "waiting")
        {
            return false;
        }

        entity.Status = "cancelled";
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    public async Task<int> TryPromoteNextAsync(string licenseId, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AchillesDbContext>();
        var engine = scope.ServiceProvider.GetRequiredService<LeaseEngine>();
        var webhooks = scope.ServiceProvider.GetService<Webhooks.IWebhookDispatcher>();

        var now = _timeProvider.GetUtcNow();
        var waitingTickets = await db.QueueTickets
            .Where(q => q.LicenseId == licenseId && q.Status == "waiting" && q.ExpiresAt > now)
            .OrderByDescending(q => q.Priority)
            .ThenBy(q => q.CreatedAt)
            .Take(10)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        int promotedCount = 0;

        foreach (var ticket in waitingTickets)
        {
            var features = JsonSerializer.Deserialize<List<string>>(ticket.FeaturesJson);
            var cmd = new CheckoutCommand(
                ticket.LicenseId,
                ticket.Fingerprint,
                ticket.MachineId,
                ticket.Quantity,
                features,
                ticket.Ticket,
                AllowQueue: false,
                Ttl: TimeSpan.FromMinutes(10));

            var result = await engine.CheckoutAsync(cmd, ct).ConfigureAwait(false);
            if (result.IsSuccess && result.Allocations is { Count: > 0 } && result.Tokens is { Count: > 0 })
            {
                var alloc = result.Allocations[0];
                ticket.Status = "ready";
                ticket.PromotedLeaseId = alloc.LeaseId;
                ticket.PromotedToken = result.Tokens[0];
                ticket.PromotedSeatNo = alloc.SeatNo;
                ticket.PromotedExpiresAt = alloc.ExpiresAt;

                promotedCount++;
                if (_logger.IsEnabled(LogLevel.Information))
                {
                    _logger.LogInformation("Promoted queue ticket {Ticket} (Priority {Priority}) to lease {LeaseId}", ticket.Ticket, ticket.Priority, alloc.LeaseId);
                }

                if (webhooks != null)
                {
                    await webhooks.PublishEventAsync("queue.promoted", new
                    {
                        ticket = ticket.Ticket,
                        licenseId = ticket.LicenseId,
                        leaseId = alloc.LeaseId,
                        seat = alloc.SeatNo,
                        priority = ticket.Priority
                    }, null, ct).ConfigureAwait(false);
                }
            }
            else
            {
                // Pool still exhausted
                break;
            }
        }

        if (promotedCount > 0)
        {
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return promotedCount;
    }

    public async Task<bool> PromoteTicketAsync(string ticket, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AchillesDbContext>();
        var engine = scope.ServiceProvider.GetRequiredService<LeaseEngine>();
        var webhooks = scope.ServiceProvider.GetService<Webhooks.IWebhookDispatcher>();

        var entity = await db.QueueTickets.FirstOrDefaultAsync(q => q.Ticket == ticket, ct).ConfigureAwait(false);
        if (entity is null || entity.Status != "waiting")
        {
            return false;
        }

        var features = JsonSerializer.Deserialize<List<string>>(entity.FeaturesJson);
        var cmd = new CheckoutCommand(
            entity.LicenseId,
            entity.Fingerprint,
            entity.MachineId,
            entity.Quantity,
            features,
            entity.Ticket,
            AllowQueue: false,
            Ttl: TimeSpan.FromMinutes(10));

        var result = await engine.CheckoutAsync(cmd, ct).ConfigureAwait(false);
        if (result.IsSuccess && result.Allocations is { Count: > 0 } && result.Tokens is { Count: > 0 })
        {
            var alloc = result.Allocations[0];
            entity.Status = "ready";
            entity.PromotedLeaseId = alloc.LeaseId;
            entity.PromotedToken = result.Tokens[0];
            entity.PromotedSeatNo = alloc.SeatNo;
            entity.PromotedExpiresAt = alloc.ExpiresAt;

            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            if (webhooks != null)
            {
                await webhooks.PublishEventAsync("queue.promoted", new
                {
                    ticket = entity.Ticket,
                    licenseId = entity.LicenseId,
                    leaseId = alloc.LeaseId,
                    seat = alloc.SeatNo,
                    priority = entity.Priority
                }, null, ct).ConfigureAwait(false);
            }
            return true;
        }

        return false;
    }

    public async Task<List<QueueTicketItemDto>> GetTicketsAsync(string? licenseId = null, string? status = null, int limit = 50, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AchillesDbContext>();

        var query = db.QueueTickets.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(licenseId))
        {
            query = query.Where(q => q.LicenseId == licenseId);
        }
        if (!string.IsNullOrWhiteSpace(status))
        {
            query = query.Where(q => q.Status == status);
        }

        var tickets = await query
            .OrderByDescending(q => q.Priority)
            .ThenBy(q => q.CreatedAt)
            .Take(limit)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var list = new List<QueueTicketItemDto>(tickets.Count);
        for (int i = 0; i < tickets.Count; i++)
        {
            var t = tickets[i];
            list.Add(new QueueTicketItemDto
            {
                Ticket = t.Ticket,
                LicenseId = t.LicenseId,
                Fingerprint = t.Fingerprint,
                MachineId = t.MachineId,
                UserId = t.UserId,
                Quantity = t.Quantity,
                Priority = t.Priority,
                Status = t.Status,
                Position = t.Status == "waiting" ? (i + 1) : 0,
                CreatedAt = t.CreatedAt,
                ExpiresAt = t.ExpiresAt
            });
        }

        return list;
    }

    public async Task<int> SweepExpiredTicketsAsync(CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AchillesDbContext>();

        var now = _timeProvider.GetUtcNow();
        var expired = await db.QueueTickets
            .Where(q => q.Status == "waiting" && q.ExpiresAt < now)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (expired.Count == 0) return 0;

        foreach (var t in expired)
        {
            t.Status = "expired";
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return expired.Count;
    }
}
