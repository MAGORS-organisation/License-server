using System.Threading.Channels;

namespace Achilles.Domain.Webhooks;

#pragma warning disable CA1711 // Identifiers should not have incorrect suffix (Queue)
public interface IWebhookQueue
{
    bool TryEnqueue(WebhookEvent evt);
    ValueTask EnqueueAsync(WebhookEvent evt, CancellationToken ct = default);
    IAsyncEnumerable<WebhookEvent> ReadAllAsync(CancellationToken ct = default);
}

public sealed class WebhookQueue : IWebhookQueue
{
    private readonly Channel<WebhookEvent> _channel;

    public WebhookQueue(int capacity = 10000)
    {
        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = false,
            SingleWriter = false
        };
        _channel = Channel.CreateBounded<WebhookEvent>(options);
    }

    public bool TryEnqueue(WebhookEvent evt)
    {
        ArgumentNullException.ThrowIfNull(evt);
        return _channel.Writer.TryWrite(evt);
    }

    public ValueTask EnqueueAsync(WebhookEvent evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);
        return _channel.Writer.WriteAsync(evt, ct);
    }

    public IAsyncEnumerable<WebhookEvent> ReadAllAsync(CancellationToken ct = default)
    {
        return _channel.Reader.ReadAllAsync(ct);
    }
}
#pragma warning restore CA1711
