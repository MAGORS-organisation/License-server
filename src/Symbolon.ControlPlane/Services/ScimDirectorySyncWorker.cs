using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Symbolon.Data;
using Symbolon.Data.Entities;
using Symbolon.Domain;
using Symbolon.Protocol.Scim;
using Symbolon.Protocol.Sso;
using Symbolon.Protocol.Tracing;

namespace Symbolon.ControlPlane.Services;

public interface IScimDirectorySyncWorker
{
    Task<DirectorySyncResultDto> RunSyncCycleAsync(string? tenantId = null, CancellationToken ct = default);
    DirectorySyncResultDto? GetLastResult();
}

#pragma warning disable CA1031 // Catch general exceptions in background worker loop
public sealed class ScimDirectorySyncWorker : BackgroundService, IScimDirectorySyncWorker
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScimDirectorySyncWorker> _logger;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _interval;
    private DirectorySyncResultDto? _lastResult;

    public ScimDirectorySyncWorker(
        IServiceScopeFactory scopeFactory,
        ILogger<ScimDirectorySyncWorker> logger,
        TimeProvider? timeProvider = null,
        TimeSpan? interval = null)
    {
        _scopeFactory = scopeFactory ?? throw new ArgumentNullException(nameof(scopeFactory));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _timeProvider = timeProvider ?? TimeProvider.System;
        _interval = interval ?? TimeSpan.FromSeconds(30);
    }

    public DirectorySyncResultDto? GetLastResult() => _lastResult;

    public async Task<DirectorySyncResultDto> RunSyncCycleAsync(string? tenantId = null, CancellationToken ct = default)
    {
        using var activity = SymbolonTracing.ActivitySource.StartActivity("symbolon.scim.directory_sync");
        activity?.SetTag("tenant_id", tenantId ?? "all");

        var now = _timeProvider.GetUtcNow();
        int inactiveUsersScanned = 0;
        int reclaimedSeats = 0;
        int reclaimedAssignments = 0;
        int synchronizedGroups = 0;

        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<SymbolonDbContext>();
            var leaseEngine = scope.ServiceProvider.GetRequiredService<LeaseEngine>();
            var auditLedger = scope.ServiceProvider.GetRequiredService<IAuditLedger>();

            // 1. Scan deprovisioned/inactive users
            var usersQuery = db.ScimUsers
                .Include(u => u.GroupMemberships)
                .Where(u => !u.Active);

            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                usersQuery = usersQuery.Where(u => u.TenantId == tenantId);
            }

            var inactiveUsers = await usersQuery.ToListAsync(ct).ConfigureAwait(false);
            inactiveUsersScanned = inactiveUsers.Count;

            foreach (var user in inactiveUsers)
            {
                var identifiers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    user.Id,
                    user.UserName
                };
                if (!string.IsNullOrWhiteSpace(user.ExternalId))
                {
                    identifiers.Add(user.ExternalId);
                }
                if (!string.IsNullOrWhiteSpace(user.Email))
                {
                    identifiers.Add(user.Email);
                }

                // A. Reclaim active floating seats
                var candidateSeats = await db.Seats
                    .Include(s => s.License)
                    .Where(s => s.License != null &&
                                s.License.TenantId == user.TenantId &&
                                s.LeaseId != null &&
                                s.ExpiresAt > now)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                foreach (var seat in candidateSeats)
                {
                    bool isMatch = (seat.UserId != null && identifiers.Contains(seat.UserId)) ||
                                   (seat.ReservedFor != null && identifiers.Contains(seat.ReservedFor));

                    if (isMatch && !string.IsNullOrWhiteSpace(seat.LeaseId))
                    {
                        await leaseEngine.ReleaseAsync(seat.LeaseId, ct).ConfigureAwait(false);
                        reclaimedSeats++;

                        await auditLedger.AppendAsync(new AuditEvent(
                            Type: "scim.user.reconciled_deprovisioned",
                            LicenseId: seat.LicenseId,
                            LeaseId: seat.LeaseId,
                            Fingerprint: seat.HolderFp != null ? Convert.ToHexString(seat.HolderFp) : seat.MachineId,
                            Timestamp: now,
                            Detail: $"Directory sync reconciled inactive user: {user.UserName} ({user.Id})",
                            TenantId: user.TenantId), ct).ConfigureAwait(false);
                    }
                }

                // B. Reclaim named user assignments
                var namedAssignments = await db.LicenseUsers
                    .Include(lu => lu.License)
                    .Where(lu => lu.License != null &&
                                lu.License.TenantId == user.TenantId &&
                                identifiers.Contains(lu.UserId))
                    .ToListAsync(ct)
                    .ConfigureAwait(false);

                foreach (var assignment in namedAssignments)
                {
                    db.LicenseUsers.Remove(assignment);
                    reclaimedAssignments++;

                    await auditLedger.AppendAsync(new AuditEvent(
                        Type: "scim.user.named_license_revoked",
                        LicenseId: assignment.LicenseId,
                        LeaseId: null,
                        Fingerprint: null,
                        Timestamp: now,
                        Detail: $"Directory sync revoked named license assignment for inactive user: {user.UserName}",
                        TenantId: user.TenantId), ct).ConfigureAwait(false);
                }
            }

            // 2. Synchronize groups
            var groupsQuery = db.ScimGroups.Include(g => g.Members).AsQueryable();
            if (!string.IsNullOrWhiteSpace(tenantId))
            {
                groupsQuery = groupsQuery.Where(g => g.TenantId == tenantId);
            }
            var groups = await groupsQuery.ToListAsync(ct).ConfigureAwait(false);
            synchronizedGroups = groups.Count;

            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            var result = new DirectorySyncResultDto
            {
                Timestamp = now,
                InactiveUsersScanned = inactiveUsersScanned,
                ReclaimedSeatsCount = reclaimedSeats,
                ReclaimedAssignmentsCount = reclaimedAssignments,
                SynchronizedGroupsCount = synchronizedGroups,
                IsSuccess = true,
                ErrorMessage = null
            };

            _lastResult = result;

            if (_logger.IsEnabled(LogLevel.Information))
            {
                _logger.LogInformation("Directory sync completed: {Users} inactive users scanned, {Seats} seats reclaimed, {Assignments} assignments revoked.",
                    inactiveUsersScanned, reclaimedSeats, reclaimedAssignments);
            }

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during SCIM directory sync cycle.");
            var errResult = new DirectorySyncResultDto
            {
                Timestamp = now,
                InactiveUsersScanned = inactiveUsersScanned,
                ReclaimedSeatsCount = reclaimedSeats,
                ReclaimedAssignmentsCount = reclaimedAssignments,
                SynchronizedGroupsCount = synchronizedGroups,
                IsSuccess = false,
                ErrorMessage = ex.Message
            };
            _lastResult = errResult;
            return errResult;
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("ScimDirectorySyncWorker background service started with interval {Interval}.", _interval);
        }

        using var timer = new PeriodicTimer(_interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                {
                    await RunSyncCycleAsync(tenantId: null, stoppingToken).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in ScimDirectorySyncWorker background loop.");
            }
        }

        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("ScimDirectorySyncWorker background service stopped.");
        }
    }
}
