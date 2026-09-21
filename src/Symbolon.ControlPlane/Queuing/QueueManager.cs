using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Symbolon.Data;
using Symbolon.Data.Entities;
using Symbolon.Domain;
using Symbolon.Protocol;

namespace Symbolon.ControlPlane.Queuing;

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
        CancellationToken ct = default);

    Task<QueueStatusResponseDto?> GetStatusAsync(string ticket, CancellationToken ct = default);

    Task<bool> CancelAsync(string ticket, CancellationToken ct = default);

    Task<int> TryPromoteNextAsync(string licenseId, CancellationToken ct = default);
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
        CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SymbolonDbContext>();

        var now = _timeProvider.GetUtcNow();
        var expiresAt = now.Add(ttl);
        string ticketId = $"q_{Guid.NewGuid():N}";

        var ticket = new QueueTicketEntity
        {
            Ticket = ticketId,
            LicenseId = licenseId,
            Fingerprint = fingerprint,
            MachineId = machineId,
            UserId = userId,
            Quantity = quantity,
            FeaturesJson = JsonSerializer.Serialize(features ?? []),
            Status = "waiting",
            ExpiresAt = expiresAt,
            CreatedAt = now
        };

        db.QueueTickets.Add(ticket);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("Client enqueued ticket {Ticket} for license {LicenseId}", ticketId, licenseId);
        }
        return ticket;
    }

    public async Task<QueueStatusResponseDto?> GetStatusAsync(string ticket, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SymbolonDbContext>();

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
                LeaseId = entity.PromotedLeaseId,
                Token = entity.PromotedToken,
                Seat = entity.PromotedSeatNo,
                ExpiresAt = entity.PromotedExpiresAt
            };
        }

        if (entity.Status == "waiting")
        {
            int position = await db.QueueTickets
                .Where(q => q.LicenseId == entity.LicenseId && q.Status == "waiting" && q.CreatedAt < entity.CreatedAt)
                .CountAsync(ct)
                .ConfigureAwait(false) + 1;

            return new QueueStatusResponseDto
            {
                Ticket = entity.Ticket,
                Status = "waiting",
                Position = position
            };
        }

        return new QueueStatusResponseDto
        {
            Ticket = entity.Ticket,
            Status = entity.Status
        };
    }

    public async Task<bool> CancelAsync(string ticket, CancellationToken ct = default)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<SymbolonDbContext>();

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
        var db = scope.ServiceProvider.GetRequiredService<SymbolonDbContext>();
        var engine = scope.ServiceProvider.GetRequiredService<LeaseEngine>();

        var now = _timeProvider.GetUtcNow();
        var waitingTickets = await db.QueueTickets
            .Where(q => q.LicenseId == licenseId && q.Status == "waiting" && q.ExpiresAt > now)
            .OrderBy(q => q.CreatedAt)
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
                    _logger.LogInformation("Promoted queue ticket {Ticket} to lease {LeaseId}", ticket.Ticket, alloc.LeaseId);
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
}
