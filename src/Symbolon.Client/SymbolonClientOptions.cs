using Symbolon.Crypto;
using Symbolon.Format;

namespace Symbolon.Client;

public sealed class SymbolonClientOptions
{
    public required Uri ServerUri { get; init; }
    public required string LicenseKey { get; init; }
    public required string ProductCode { get; init; }
    public IKeyRing? TrustedKeys { get; init; }
    public Strictness Strictness { get; init; } = Strictness.Auto;
    public HttpClient? HttpClient { get; init; }
    public TimeProvider TimeProvider { get; init; } = TimeProvider.System;
    public TimeSpan HeartbeatInterval { get; init; } = TimeSpan.FromMinutes(4);
    public TimeSpan GracePeriod { get; init; } = TimeSpan.FromHours(4);
    public IReadOnlyDictionary<string, string>? CustomFingerprint { get; init; }
}
