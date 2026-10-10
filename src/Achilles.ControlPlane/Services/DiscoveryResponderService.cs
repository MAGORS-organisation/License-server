using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Achilles.Protocol;

namespace Achilles.ControlPlane.Services;

#pragma warning disable CA1031 // Do not catch general exception types in background worker loop
/// <summary>
/// Background service that listens for UDP zero-config discovery probes and announces ControlPlane availability.
/// </summary>
[SuppressMessage("Performance", "CA1873:Avoid using object array allocation", Justification = "Discovery responder logging")]
public sealed class DiscoveryResponderService : BackgroundService
{
    private readonly IConfiguration _config;
    private readonly ILogger<DiscoveryResponderService> _logger;

    public DiscoveryResponderService(IConfiguration config, ILogger<DiscoveryResponderService> logger)
    {
        _config = config;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (string.Equals(_config["Discovery:Enabled"], "false", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogInformation("Zero-config discovery responder is disabled by configuration.");
            return;
        }

        int port = SymbolonDiscoveryConstants.DefaultPort;
        if (int.TryParse(_config["Discovery:Port"], out int customPort) && customPort > 0)
        {
            port = customPort;
        }

        string nodeId = _config["Discovery:NodeId"] ?? $"{Environment.MachineName.ToUpperInvariant()}-CP";
        string serverUrl = _config["Discovery:ServerUrl"] ?? _config["urls"] ?? "http://localhost:5000";
        // If serverUrl has multiple semi-colon separated URLs, use the first one
        if (serverUrl.Contains(';', StringComparison.Ordinal))
        {
            serverUrl = serverUrl.Split(';')[0];
        }
        string? clusterId = _config["Discovery:ClusterId"] ?? "cp-cluster-1";

        using var udpClient = new UdpClient();
        try
        {
            udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, port));
            _logger.LogInformation("DiscoveryResponderService listening on UDP port {Port} for node {NodeId} ({ServerUrl})", port, nodeId, serverUrl);
        }
        catch (SocketException ex)
        {
            _logger.LogWarning(ex, "Failed to bind UDP discovery responder to port {Port}. Zero-config discovery disabled.", port);
            return;
        }

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var result = await udpClient.ReceiveAsync(stoppingToken).ConfigureAwait(false);
                DiscoveryProbePacket? probe = null;

                try
                {
                    probe = JsonSerializer.Deserialize(result.Buffer, AchillesProtocolJsonContext.Default.DiscoveryProbePacket);
                }
                catch
                {
                    // Not a valid probe packet, ignore
                }

                if (probe == null || !string.Equals(probe.Magic, SymbolonDiscoveryConstants.MagicProbe, StringComparison.Ordinal))
                {
                    continue;
                }

                _logger.LogDebug("Received discovery probe from {RemoteEndPoint} (ClientId: {ClientId})", result.RemoteEndPoint, probe.ClientId);

                var announcement = new DiscoveryAnnouncementPacket
                {
                    Magic = SymbolonDiscoveryConstants.MagicServer,
                    NodeId = nodeId,
                    ServerUrl = serverUrl,
                    ServerType = "controlplane",
                    ClusterId = clusterId,
                    Version = "1.0.0",
                    Timestamp = DateTimeOffset.UtcNow
                };

                byte[] responseBytes = JsonSerializer.SerializeToUtf8Bytes(announcement, AchillesProtocolJsonContext.Default.DiscoveryAnnouncementPacket);
                await udpClient.SendAsync(responseBytes, result.RemoteEndPoint, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unexpected error in DiscoveryResponderService receive loop.");
            }
        }
    }
}
#pragma warning restore CA1031
