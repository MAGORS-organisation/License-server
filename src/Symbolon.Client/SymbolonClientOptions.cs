using Symbolon.Client.Discovery;
using Symbolon.Client.Revocation;
using Symbolon.Crypto;
using Symbolon.Format;

namespace Symbolon.Client;

public sealed class SymbolonClientOptions
{
    /// <summary>
    /// Primary server endpoint URI.
    /// Optional if <see cref="ServerUris"/>, <see cref="FailoverPool"/>, or <see cref="AutoDiscover"/> is specified.
    /// </summary>
    public Uri? ServerUri { get; init; }

    /// <summary>
    /// Ordered list of candidate server URIs for high-availability failover.
    /// </summary>
    public IReadOnlyList<Uri>? ServerUris { get; init; }

    /// <summary>
    /// Custom failover pool instance.
    /// </summary>
    public ServerFailoverPool? FailoverPool { get; init; }

    /// <summary>
    /// If true, performs local network discovery via UDP probe or environment variable resolution.
    /// </summary>
    public bool AutoDiscover { get; init; }

    public required string LicenseKey { get; init; }
    public required string ProductCode { get; init; }
    public IKeyRing? TrustedKeys { get; init; }
    public Strictness Strictness { get; init; } = Strictness.Auto;
    public HttpClient? HttpClient { get; init; }
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromMinutes(4);
    public TimeSpan GracePeriod { get; init; } = TimeSpan.FromHours(4);
    public IReadOnlyDictionary<string, string>? CustomFingerprint { get; init; }

    /// <summary>
    /// Optional revocation cache instance for real-time revocation enforcement (RVL-12 to RVL-16).
    /// </summary>
    public RevocationCache? RevocationCache { get; init; }

    /// <summary>
    /// If true, enables enterprise queuing when all floating seats are occupied (FLT-31).
    /// </summary>
    public bool AllowQueue { get; init; }

    /// <summary>
    /// Maximum duration to wait in queue before timing out and cancelling the ticket (default 2 minutes).
    /// </summary>
    public TimeSpan MaxQueueWait { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Explicit request priority for queuing (higher numbers get scheduled first).
    /// </summary>
    public int Priority { get; init; }

    /// <summary>
    /// Optional user ID for named user licensing and VIP queue priority resolution.
    /// </summary>
    public string? UserId { get; init; }

    /// <summary>
    /// Optional machine ID identifier.
    /// </summary>
    public string? MachineId { get; init; }
}
