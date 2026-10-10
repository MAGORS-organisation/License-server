using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Threading.Channels;

namespace Symbolon.ControlPlane.Events;

public sealed record ClusterEventMessage(
    string Type,
    object Data,
    DateTimeOffset Timestamp
);

public interface IClusterEventBroadcaster
{
    void Publish(string type, object data);
    IAsyncEnumerable<ClusterEventMessage> SubscribeAsync(CancellationToken cancellationToken);
}

public sealed class ClusterEventBroadcaster : IClusterEventBroadcaster
{
    private readonly ConcurrentDictionary<Guid, Channel<ClusterEventMessage>> _subscribers = new();

    public void Publish(string type, object data)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentNullException.ThrowIfNull(data);

        var message = new ClusterEventMessage(type, data, DateTimeOffset.UtcNow);

        foreach (var channel in _subscribers.Values)
        {
            channel.Writer.TryWrite(message);
        }
    }

    public async IAsyncEnumerable<ClusterEventMessage> SubscribeAsync([EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var id = Guid.NewGuid();
        var channel = Channel.CreateBounded<ClusterEventMessage>(new BoundedChannelOptions(256)
        {
            FullMode = BoundedChannelFullMode.DropOldest,
            SingleReader = true,
            SingleWriter = false
        });

        _subscribers.TryAdd(id, channel);

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                ClusterEventMessage item;
                try
                {
                    item = await channel.Reader.ReadAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    yield break;
                }

                yield return item;
            }
        }
        finally
        {
            _subscribers.TryRemove(id, out _);
        }
    }
}
