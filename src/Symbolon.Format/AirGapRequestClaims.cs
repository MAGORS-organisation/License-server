using System.Text.Json.Serialization;

namespace Symbolon.Format;

/// <summary>
/// Claims encoded inside an Air-Gapped Grant Request (.symreq) according to spec/07-floating-protokol.md §7.7 (FLT-32).
/// </summary>
public sealed record AirGapRequestClaims
{
    [JsonPropertyName("iss")]
    public required string Iss { get; init; } // relayId

    [JsonPropertyName("sub")]
    public required string Sub { get; init; } // licenseKey

    [JsonPropertyName("jti")]
    public required string Jti { get; init; } // request UUID / nonce

    [JsonPropertyName("iat")]
    public required long Iat { get; init; }

    [JsonPropertyName("exp")]
    public required long Exp { get; init; }

    [JsonPropertyName("symreq")]
    public required AirGapRequestPayload Symreq { get; init; }
}

public sealed record AirGapRequestPayload
{
    [JsonPropertyName("v")]
    public int V { get; init; } = 1;

    [JsonPropertyName("relayId")]
    public required string RelayId { get; init; }

    [JsonPropertyName("licenseKey")]
    public required string LicenseKey { get; init; }

    [JsonPropertyName("requestedSeats")]
    public required int RequestedSeats { get; init; }

    [JsonPropertyName("lastSeq")]
    public required long LastSeq { get; init; }

    /// <summary>
    /// Cryptographic hash-chain root of local audit ledger since last grant (FLT-32, FLT-33).
    /// </summary>
    [JsonPropertyName("usageDigest")]
    public required string UsageDigest { get; init; }

    /// <summary>
    /// Anti-replay nonce (FLT-34).
    /// </summary>
    [JsonPropertyName("nonce")]
    public required string Nonce { get; init; }
}
