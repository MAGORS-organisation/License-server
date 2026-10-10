using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Symbolon.ControlPlane.Events;
using Xunit;

namespace Symbolon.ControlPlane.Tests;

public sealed class VisualizerAndSseTests : IClassFixture<ControlPlaneFactory>
{
    private readonly HttpClient _client;

    public VisualizerAndSseTests(ControlPlaneFactory factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Visualizer_Endpoint_Returns_Html_With_Dual_Theme_Support()
    {
        var response = await _client.GetAsync("/visualizer");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("text/html");

        string html = await response.Content.ReadAsStringAsync();
        html.Should().Contain("SYMBOLON LIVE CLUSTER & MERKLE VISUALIZER");
        html.Should().Contain("data-theme=\"retro\"");
        html.Should().Contain("CRT Phosphor");
        html.Should().Contain("Modern Dark");
        html.Should().Contain("Cyberpunk");
        html.Should().Contain("Apple iOS");
        html.Should().Contain("/v1/events");
        html.Should().Contain("/v1/cluster/topology");
    }

    [Fact]
    public async Task ClusterTopology_Endpoint_Returns_Valid_Json()
    {
        var response = await _client.GetAsync("/v1/cluster/topology");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;

        root.GetProperty("clusterId").GetString().Should().NotBeNullOrWhiteSpace();
        root.GetProperty("nodes").GetArrayLength().Should().BeGreaterThanOrEqualTo(1);
        root.GetProperty("merkleTree").GetProperty("rootHash").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Sse_Events_Endpoint_Starts_Streaming_With_Ready_Event()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var req = new HttpRequestMessage(HttpMethod.Get, "/v1/events");
        using var res = await _client.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        res.Content.Headers.ContentType?.MediaType.Should().Be("text/event-stream");

        using var stream = await res.Content.ReadAsStreamAsync(cts.Token);
        using var reader = new StreamReader(stream);

        // Read first few lines of SSE stream
        string? line1 = await reader.ReadLineAsync(cts.Token);
        string? line2 = await reader.ReadLineAsync(cts.Token);

        (line1 ?? line2).Should().Contain("event: ready");
    }

    [Fact]
    public async Task ClusterEventBroadcaster_Direct_Publish_And_Subscribe_Works()
    {
        var broadcaster = new ClusterEventBroadcaster();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));

        var enumerator = broadcaster.SubscribeAsync(cts.Token).GetAsyncEnumerator(cts.Token);

        var moveNextTask = enumerator.MoveNextAsync().AsTask();
        broadcaster.Publish("lease.acquired", new { leaseId = "test-123", seat = 1 });

        bool hasItem = await moveNextTask;
        hasItem.Should().BeTrue();
        enumerator.Current.Type.Should().Be("lease.acquired");
    }
}
