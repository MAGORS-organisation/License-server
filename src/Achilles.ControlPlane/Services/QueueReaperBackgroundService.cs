using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Achilles.ControlPlane.Queuing;
using Achilles.Data;

namespace Achilles.ControlPlane.Services;

#pragma warning disable CA1031 // Do not catch general exception types in background worker loop
public sealed class QueueReaperBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IQueueManager _queueManager;
    private readonly ILogger<QueueReaperBackgroundService> _logger;
    private readonly TimeSpan _interval = TimeSpan.FromSeconds(5);

    public QueueReaperBackgroundService(
        IServiceScopeFactory scopeFactory,
        IQueueManager queueManager,
        ILogger<QueueReaperBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _queueManager = queueManager;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("QueueReaperBackgroundService started monitoring queue tickets and seat promotions.");

        using var timer = new PeriodicTimer(_interval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                {
                    // 1. Sweep expired queue tickets
                    int swept = await _queueManager.SweepExpiredTicketsAsync(stoppingToken).ConfigureAwait(false);
                    if (swept > 0 && _logger.IsEnabled(LogLevel.Information))
                    {
                        _logger.LogInformation("Swept {Count} expired queue tickets.", swept);
                    }

                    // 2. Identify licenses with waiting tickets and attempt promotions
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AchillesDbContext>();

                    var pendingLicenseIds = await db.QueueTickets
                        .AsNoTracking()
                        .Where(q => q.Status == "waiting")
                        .Select(q => q.LicenseId)
                        .Distinct()
                        .ToListAsync(stoppingToken)
                        .ConfigureAwait(false);

                    foreach (var licenseId in pendingLicenseIds)
                    {
                        int promoted = await _queueManager.TryPromoteNextAsync(licenseId, stoppingToken).ConfigureAwait(false);
                        if (promoted > 0 && _logger.IsEnabled(LogLevel.Information))
                        {
                            _logger.LogInformation("Auto-promoted {Count} queue tickets for license {LicenseId}.", promoted, licenseId);
                        }
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in QueueReaperBackgroundService worker loop.");
            }
        }

        _logger.LogInformation("QueueReaperBackgroundService stopped.");
    }
}
