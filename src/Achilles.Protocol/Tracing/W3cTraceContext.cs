using System.Diagnostics;
using System.Globalization;

namespace Achilles.Protocol.Tracing;

/// <summary>
/// Pomocná trieda pre manipuláciu a propagáciu štandardných W3C TraceContext hlavičiek (traceparent, tracestate).
/// </summary>
public static class W3cTraceContext
{
    public const string TraceParentHeader = "traceparent";
    public const string TraceStateHeader = "tracestate";

    /// <summary>
    /// Vytvorí W3C traceparent reťazec z aktívnej aktivity alebo vygeneruje nový.
    /// Formát: 00-{traceId}-{spanId}-{flags}
    /// </summary>
    public static string CreateTraceparent(Activity? activity = null)
    {
        if (activity is not null && activity.TraceId != default && activity.SpanId != default)
        {
            byte flags = (byte)((activity.ActivityTraceFlags & ActivityTraceFlags.Recorded) != 0 ? 1 : 0);
            return $"00-{activity.TraceId.ToHexString()}-{activity.SpanId.ToHexString()}-{flags:x2}";
        }

        var traceId = ActivityTraceId.CreateRandom();
        var spanId = ActivitySpanId.CreateRandom();
        return $"00-{traceId.ToHexString()}-{spanId.ToHexString()}-01";
    }

    /// <summary>
    /// Pokúsi sa rozparsovať W3C traceparent hlavičku.
    /// </summary>
    public static bool TryParseTraceparent(
        string? headerValue,
        out string traceId,
        out string spanId,
        out byte flags)
    {
        traceId = string.Empty;
        spanId = string.Empty;
        flags = 0;

        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return false;
        }

        string trimmed = headerValue.Trim();
        string[] parts = trimmed.Split('-');
        if (parts.Length != 4)
        {
            return false;
        }

        // Version check: 00 or compatible
        if (parts[0] != "00" && parts[0].Length != 2)
        {
            return false;
        }

        // Trace ID check: 32 hex chars, not all zeros
        if (parts[1].Length != 32 || parts[1] == "00000000000000000000000000000000")
        {
            return false;
        }

        // Span ID check: 16 hex chars, not all zeros
        if (parts[2].Length != 16 || parts[2] == "0000000000000000")
        {
            return false;
        }

        // Flags check: 2 hex chars
        if (parts[3].Length != 2 || !byte.TryParse(parts[3], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out flags))
        {
            return false;
        }

        traceId = parts[1];
        spanId = parts[2];
        return true;
    }

    /// <summary>
    /// Vloží W3C hlavičky do HTTP požiadavky cez zadaný delegát.
    /// </summary>
    public static void Inject(Activity? activity, Action<string, string> setHeader)
    {
        ArgumentNullException.ThrowIfNull(setHeader);

        string traceParent = CreateTraceparent(activity);
        setHeader(TraceParentHeader, traceParent);

        if (activity?.TraceStateString is not null)
        {
            setHeader(TraceStateHeader, activity.TraceStateString);
        }
    }

    /// <summary>
    /// Extrahuje ActivityContext z W3C hlavičiek pre nadviazanie distribuovanej stopy.
    /// </summary>
    public static bool TryExtractContext(
        Func<string, string?> getHeader,
        out ActivityContext context)
    {
        ArgumentNullException.ThrowIfNull(getHeader);

        string? traceParent = getHeader(TraceParentHeader);
        if (TryParseTraceparent(traceParent, out string traceIdStr, out string spanIdStr, out byte flagsByte))
        {
            var traceId = ActivityTraceId.CreateFromString(traceIdStr.AsSpan());
            var spanId = ActivitySpanId.CreateFromString(spanIdStr.AsSpan());
            var flags = (flagsByte & 0x01) != 0 ? ActivityTraceFlags.Recorded : ActivityTraceFlags.None;
            string? traceState = getHeader(TraceStateHeader);

            context = new ActivityContext(traceId, spanId, flags, traceState, isRemote: true);
            return true;
        }

        context = default;
        return false;
    }
}
