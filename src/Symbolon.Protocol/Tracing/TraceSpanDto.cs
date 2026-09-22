namespace Symbolon.Protocol.Tracing;

/// <summary>
/// Prenosový dátový objekt reprezentujúci OpenTelemetry W3C distribuovanú stopu (Span).
/// </summary>
public sealed record TraceSpanDto(
    string TraceId,
    string SpanId,
    string? ParentSpanId,
    string Name,
    DateTimeOffset StartTime,
    double DurationMs,
    string Status,
    IReadOnlyDictionary<string, string> Tags);
