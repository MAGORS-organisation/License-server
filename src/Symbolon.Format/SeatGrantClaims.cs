using System.Text.Json.Serialization;
using Symbolon.Crypto;

namespace Symbolon.Format;

/// <summary>
/// Claims encoded inside a Seat Grant (.symgrant) document according to spec/05-seat-grant.md.
/// </summary>
public sealed record SeatGrantDocumentClaims
{
    [JsonPropertyName("iss")]
    public required string Iss { get; init; }

    [JsonPropertyName("sub")]
    public required string Sub { get; init; }

    [JsonPropertyName("aud")]
    public required string Aud { get; init; }

    [JsonPropertyName("jti")]
    public required string Jti { get; init; }

    [JsonPropertyName("iat")]
    public required long Iat { get; init; }

    [JsonPropertyName("nbf")]
    public required long Nbf { get; init; }

    [JsonPropertyName("exp")]
    public required long Exp { get; init; }

    [JsonPropertyName("symgrant")]
    public required SeatGrantPayload Symgrant { get; init; }
}

public sealed record SeatGrantPayload
{
    [JsonPropertyName("v")]
    public required int V { get; init; }

    [JsonPropertyName("seats")]
    public required int Seats { get; init; }

    /// <summary>Inclusive [from, to] seat range (GNT-3: seatRange[1] - seatRange[0] + 1 == seats).</summary>
    [JsonPropertyName("seatRange")]
    public required IReadOnlyList<int> SeatRange { get; init; }

    [JsonPropertyName("seq")]
    public required long Seq { get; init; }

    [JsonPropertyName("supersedes")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? Supersedes { get; init; }

    [JsonPropertyName("entitlements")]
    public required IReadOnlyList<string> Entitlements { get; init; }

    [JsonPropertyName("leasePolicy")]
    public required LeasePolicyClaim LeasePolicy { get; init; }

    [JsonPropertyName("leaseKey")]
    public required JsonWebKeyDto LeaseKey { get; init; }

    [JsonPropertyName("offlineExtension")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public OfflineExtensionClaim? OfflineExtension { get; init; }
}

public sealed record OfflineExtensionClaim
{
    [JsonPropertyName("allowed")]
    public required bool Allowed { get; init; }

    [JsonPropertyName("maxExtensions")]
    public required int MaxExtensions { get; init; }
}
