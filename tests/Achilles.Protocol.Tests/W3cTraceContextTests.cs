using System.Diagnostics;
using Achilles.Protocol.Tracing;
using Xunit;

namespace Achilles.Protocol.Tests;

public class W3cTraceContextTests
{
    [Fact]
    public void CreateTraceparent_GeneratesCompliantW3cHeader()
    {
        string header = W3cTraceContext.CreateTraceparent();
        Assert.NotNull(header);
        Assert.StartsWith("00-", header, StringComparison.Ordinal);

        bool parsed = W3cTraceContext.TryParseTraceparent(header, out string traceId, out string spanId, out byte flags);
        Assert.True(parsed);
        Assert.Equal(32, traceId.Length);
        Assert.Equal(16, spanId.Length);
        Assert.Equal(1, flags);
    }

    [Theory]
    [InlineData("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01", true, "4bf92f3577b34da6a3ce929d0e0e4736", "00f067aa0ba902b7", 1)]
    [InlineData("00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-00", true, "4bf92f3577b34da6a3ce929d0e0e4736", "00f067aa0ba902b7", 0)]
    [InlineData("00-00000000000000000000000000000000-00f067aa0ba902b7-01", false, "", "", 0)] // Všetky nuly v traceId
    [InlineData("00-4bf92f3577b34da6a3ce929d0e0e4736-0000000000000000-01", false, "", "", 0)] // Všetky nuly v spanId
    [InlineData("invalid-header", false, "", "", 0)]
    [InlineData("01-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01-extra", false, "", "", 0)]
    public void TryParseTraceparent_ValidatesAndParses(string input, bool expectedResult, string expectedTraceId, string expectedSpanId, byte expectedFlags)
    {
        bool result = W3cTraceContext.TryParseTraceparent(input, out string traceId, out string spanId, out byte flags);

        Assert.Equal(expectedResult, result);
        if (expectedResult)
        {
            Assert.Equal(expectedTraceId, traceId);
            Assert.Equal(expectedSpanId, spanId);
            Assert.Equal(expectedFlags, flags);
        }
    }

    [Fact]
    public void Inject_And_Extract_RoundTrip()
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Vytvorenie lokálnej aktivity
        using var listener = new ActivityListener
        {
            ShouldListenTo = _ => true,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllData
        };
        ActivitySource.AddActivityListener(listener);

        using var source = new ActivitySource("TestTracingSource");
        using var activity = source.StartActivity("test.operation");
        Assert.NotNull(activity);

        W3cTraceContext.Inject(activity, (k, v) => headers[k] = v);

        Assert.True(headers.ContainsKey(W3cTraceContext.TraceParentHeader));
        string headerVal = headers[W3cTraceContext.TraceParentHeader];
        Assert.Contains(activity.TraceId.ToHexString(), headerVal, StringComparison.Ordinal);

        bool extracted = W3cTraceContext.TryExtractContext(k => headers.GetValueOrDefault(k), out var extractedContext);
        Assert.True(extracted);
        Assert.Equal(activity.TraceId, extractedContext.TraceId);
        Assert.Equal(activity.SpanId, extractedContext.SpanId);
    }
}
