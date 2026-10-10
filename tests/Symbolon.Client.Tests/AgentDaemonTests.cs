using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using FluentAssertions;
using Symbolon.Client.Agent;
using Symbolon.Protocol;
using Xunit;

namespace Symbolon.Client.Tests;

public sealed class AgentDaemonTests
{
    private static int GetFreePort()
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        socket.Bind(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)socket.LocalEndPoint!).Port;
    }

    [Fact]
    public async Task AgentDaemon_StartsAndRespondsToHealthAndStatus()
    {
        int port = GetFreePort();
        await using var daemon = new SymbolonAgentDaemon(port, serverUri: new Uri("http://localhost:8080"), licenseKey: null);

        daemon.Start();
        daemon.IsRunning.Should().BeTrue();

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{port}/") };

        // 1. GET /health
        var healthRes = await client.GetAsync(new Uri("/health", UriKind.Relative));
        healthRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. GET /v1/status
        var statusRes = await client.GetAsync(new Uri("/v1/status", UriKind.Relative));
        statusRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var status = await statusRes.Content.ReadFromJsonAsync<AgentStatusDto>(SymbolonProtocolJsonContext.Default.AgentStatusDto);
        status.Should().NotBeNull();
        status!.Status.Should().Be("idle");
        status.MachineId.Should().NotBeNullOrEmpty();
        status.OfflineAllowed.Should().BeTrue();

        // 3. POST /v1/release (when idle)
        var relRes = await client.PostAsync(new Uri("/v1/release", UriKind.Relative), null);
        relRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var relAction = await relRes.Content.ReadFromJsonAsync<AgentActionResponseDto>(SymbolonProtocolJsonContext.Default.AgentActionResponseDto);
        relAction.Should().NotBeNull();
        relAction!.Success.Should().BeTrue();
    }
}
