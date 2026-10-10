using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Achilles.ControlPlane.Webhooks;
using Achilles.Domain.Webhooks;

namespace Achilles.ControlPlane.Services;

#pragma warning disable CA1031 // Do not catch general exception types in background worker loop
public sealed class WebhookBackgroundService : BackgroundService
{
    private readonly IWebhookQueue _queue;
    private readonly IWebhookDispatcher _dispatcher;
    private readonly ILogger<WebhookBackgroundService> _logger;
    private readonly TimeSpan _retryInterval = TimeSpan.FromSeconds(5);

    public WebhookBackgroundService(
        IWebhookQueue queue,
        IWebhookDispatcher dispatcher,
        ILogger<WebhookBackgroundService> logger)
    {
        _queue = queue;
        _dispatcher = dispatcher;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_logger.IsEnabled(LogLevel.Information))
        {
            _logger.LogInformation("WebhookBackgroundService started listening to IWebhookQueue and retry processor.");
        }

        var queueTask = ProcessQueueAsync(stoppingToken);
        var retryTask = ProcessRetriesAsync(stoppingToken);

        await Task.WhenAll(queueTask, retryTask).ConfigureAwait(false);
    }

    private async Task ProcessQueueAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var evt in _queue.ReadAllAsync(stoppingToken).ConfigureAwait(false))
            {
                try
                {
                    await _dispatcher.PublishEventAsync(evt.EventType, evt.Data, evt.TenantId, stoppingToken).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to dispatch queued webhook event {EventId} ({EventType})", evt.Id, evt.EventType);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Graceful shutdown
        }
    }

    private async Task ProcessRetriesAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(_retryInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false))
                {
                    int retried = await _dispatcher.ProcessPendingRetriesAsync(stoppingToken).ConfigureAwait(false);
                    if (retried > 0 && _logger.IsEnabled(LogLevel.Information))
                    {
                        _logger.LogInformation("Processed {Count} webhook retries.", retried);
                    }
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in webhook retry processor loop.");
            }
        }
    }
}
#pragma warning restore CA1031
