using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Symbolon.Protocol;

namespace Symbolon.Client.Discovery;

/// <summary>
/// Client for discovering active Symbolon ControlPlane and Relay servers on the local network via UDP broadcast probes.
/// </summary>
public static class SymbolonDiscoveryClient
{
    /// <summary>
    /// Broadcasts a discovery probe on the local subnet and gathers server announcements within the timeout window.
    /// </summary>
    /// <param name="productCode">Optional product code to filter responses.</param>
    /// <param name="timeout">Maximum time to wait for server responses (default: 300ms).</param>
    /// <param name="port">Target UDP discovery port (default: 7584).</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>List of discovered servers ordered by roundtrip latency.</returns>
    public static async Task<IReadOnlyList<DiscoveredServerInfo>> DiscoverServersAsync(
        string? productCode = null,
        TimeSpan? timeout = null,
        int port = SymbolonDiscoveryConstants.DefaultPort,
        IPAddress? broadcastAddress = null,
        CancellationToken ct = default)
    {
        var waitTime = timeout ?? TimeSpan.FromMilliseconds(300);
        var discovered = new List<DiscoveredServerInfo>();
        var seenUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var udp = new UdpClient();
        udp.EnableBroadcast = true;

        var probe = new DiscoveryProbePacket
        {
            ProductCode = productCode,
            ClientId = Environment.MachineName,
            Timestamp = DateTimeOffset.UtcNow
        };

        byte[] payload = JsonSerializer.SerializeToUtf8Bytes(probe, SymbolonProtocolJsonContext.Default.DiscoveryProbePacket);
        var targetEndpoint = new IPEndPoint(broadcastAddress ?? IPAddress.Broadcast, port);

        var sw = Stopwatch.StartNew();
        await udp.SendAsync(payload, targetEndpoint, ct).ConfigureAwait(false);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        cts.CancelAfter(waitTime);

        try
        {
            while (!cts.IsCancellationRequested)
            {
                var result = await udp.ReceiveAsync(cts.Token).ConfigureAwait(false);
                double elapsedMs = sw.Elapsed.TotalMilliseconds;

                try
                {
                    string json = Encoding.UTF8.GetString(result.Buffer);
                    var announcement = JsonSerializer.Deserialize(json, SymbolonProtocolJsonContext.Default.DiscoveryAnnouncementPacket);

                    if (announcement is not null &&
                        string.Equals(announcement.Magic, SymbolonDiscoveryConstants.MagicServer, StringComparison.Ordinal) &&
                        !string.IsNullOrWhiteSpace(announcement.ServerUrl) &&
                        seenUrls.Add(announcement.ServerUrl))
                    {
                        discovered.Add(new DiscoveredServerInfo(announcement, elapsedMs, result.RemoteEndPoint.Address.ToString()));
                    }
                }
                catch (JsonException)
                {
                    // Ignore non-conforming UDP packets
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on timeout expiry
        }

        return discovered.OrderBy(d => d.RoundTripMs).ToList();
    }
}
