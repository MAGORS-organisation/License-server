using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Symbolon.Protocol;

/// <summary>
/// Constants used by the Symbolon Zero-Config Server Discovery protocol.
/// </summary>
public static class SymbolonDiscoveryConstants
{
    /// <summary>
    /// Standard UDP discovery port for Symbolon (0x1D90 = 7584).
    /// </summary>
    public const int DefaultPort = 7584;

    /// <summary>
    /// Magic identifier for discovery probe packets sent by clients.
    /// </summary>
    public const string MagicProbe = "symbolon:discover";

    /// <summary>
    /// Magic identifier for discovery announcement packets sent by servers.
    /// </summary>
    public const string MagicServer = "symbolon:server";

    /// <summary>
    /// Default environment variable name for enterprise server lists.
    /// </summary>
    public const string EnvironmentVariable = "SYMBOLON_LICENSE_SERVER";

    /// <summary>
    /// Alternate environment variable name for server lists.
    /// </summary>
    public const string EnvironmentVariableAlt = "SYMBOLON_SERVERS";
}

/// <summary>
/// UDP Probe packet sent by clients seeking active Symbolon servers on the local subnet.
/// </summary>
public sealed record DiscoveryProbePacket
{
    [JsonPropertyName("magic")]
    public string Magic { get; init; } = SymbolonDiscoveryConstants.MagicProbe;

    [JsonPropertyName("clientVersion")]
    public string ClientVersion { get; init; } = "1.0.0";

    [JsonPropertyName("productCode")]
    public string? ProductCode { get; init; }

    [JsonPropertyName("clientId")]
    public string ClientId { get; init; } = string.Empty;

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// UDP Announcement packet returned by active ControlPlane or Relay servers.
/// </summary>
public sealed record DiscoveryAnnouncementPacket
{
    [JsonPropertyName("magic")]
    public string Magic { get; init; } = SymbolonDiscoveryConstants.MagicServer;

    [JsonPropertyName("nodeId")]
    public string NodeId { get; init; } = string.Empty;

    [JsonPropertyName("serverUrl")]
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "JSON transport wire format")]
    public string ServerUrl { get; init; } = string.Empty;

    [JsonPropertyName("serverType")]
    public string ServerType { get; init; } = "controlplane"; // "controlplane" | "relay"

    [JsonPropertyName("clusterId")]
    public string? ClusterId { get; init; }

    [JsonPropertyName("version")]
    public string Version { get; init; } = "1.0.0";

    [JsonPropertyName("availableSeats")]
    public int? AvailableSeats { get; init; }

    [JsonPropertyName("totalSeats")]
    public int? TotalSeats { get; init; }

    [JsonPropertyName("timestamp")]
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
}

/// <summary>
/// Discovered server entry including roundtrip network latency.
/// </summary>
public sealed record DiscoveredServerInfo(
    DiscoveryAnnouncementPacket Announcement,
    double RoundTripMs,
    string RemoteEndpointAddress);
