using System.Diagnostics;
using Achilles.Protocol.Tracing;

namespace Achilles.ControlPlane.Observability;

/// <summary>
/// Diagnostický kruhový buffer zachytávajúci nedávne OpenTelemetry stopy (Spans)
/// generované v rámci Symbolon platformy pre zobrazenie v reálnom čase na Web TUI a v Admin API.
/// </summary>
public sealed class AchillesTraceBuffer : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly int _capacity;
    private readonly Queue<TraceSpanDto> _recentSpans = new();
    private readonly Lock _lock = new();

    public AchillesTraceBuffer(int capacity = 500)
    {
        _capacity = capacity;
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == AchillesTracing.ActivitySourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData,
            ActivityStopped = OnActivityStopped
        };
        ActivitySource.AddActivityListener(_listener);
    }

    private void OnActivityStopped(Activity activity)
    {
        var tags = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tag in activity.Tags)
        {
            if (tag.Value != null)
            {
                tags[tag.Key] = tag.Value;
            }
        }

        string status = activity.Status switch
        {
            ActivityStatusCode.Error => "ERROR",
            ActivityStatusCode.Ok => "OK",
            _ => "UNSET"
        };

        var spanDto = new TraceSpanDto(
            TraceId: activity.TraceId.ToHexString(),
            SpanId: activity.SpanId.ToHexString(),
            ParentSpanId: activity.ParentSpanId != default ? activity.ParentSpanId.ToHexString() : null,
            Name: activity.OperationName,
            StartTime: activity.StartTimeUtc,
            DurationMs: Math.Round(activity.Duration.TotalMilliseconds, 2, MidpointRounding.AwayFromZero),
            Status: status,
            Tags: tags);

        lock (_lock)
        {
            while (_recentSpans.Count >= _capacity)
            {
                _recentSpans.Dequeue();
            }
            _recentSpans.Enqueue(spanDto);
        }
    }

    /// <summary>
    /// Vráti zoznam nedávnych stôp v chronologicky obrátenom poradí (od najnovšej).
    /// </summary>
    public IReadOnlyList<TraceSpanDto> GetRecentSpans()
    {
        lock (_lock)
        {
            return _recentSpans.Reverse().ToArray();
        }
    }

    public void Dispose()
    {
        _listener.Dispose();
    }
}
