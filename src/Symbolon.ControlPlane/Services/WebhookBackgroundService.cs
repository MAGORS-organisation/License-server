using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Symbolon.ControlPlane.Webhooks;
using Symbolon.Domain.Webhooks;

namespace Symbolon.ControlPlane.Services;

#pragma warning disable CA1031 // Do not catch general exception types in background worker loop
public sealed class WebhookBackgroundService : BackgroundService
{
    private readonly IWebhookQueue _queue;
    private readonly IWebhookDispatcher _dispatcher;
    private readonly ILogger<WebhookBackgroundService> _logger;

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
        _logger.LogInformation("WebhookBackgroundService started listening to IWebhookQueue.");

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
}
#pragma warning restore CA1031
