using System.Text.Json.Serialization;

namespace Achilles.Protocol;

/// <summary>
/// Claims encoded inside a compact JWS Lease Token according to spec/04-lease-token.md (LSE-1 to LSE-6).
/// </summary>
public sealed record LeaseClaims
{
    [JsonPropertyName("iss")]
    public required string Iss { get; init; }

    [JsonPropertyName("sub")]
    public required string Sub { get; init; }

    [JsonPropertyName("jti")]
    public required string Jti { get; init; }

    [JsonPropertyName("iat")]
    public required long Iat { get; init; }

    [JsonPropertyName("exp")]
    public required long Exp { get; init; }

    [JsonPropertyName("seat")]
    public required int Seat { get; init; }

    /// <summary>
    /// sha256:{hex} hash of holder fingerprint (LSE-4).
    /// </summary>
    [JsonPropertyName("fp")]
    public required string Fp { get; init; }

    [JsonPropertyName("ent")]
    public required IReadOnlyList<string> Ent { get; init; }

    [JsonPropertyName("gnt")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Gnt { get; init; }

    /// <summary>
    /// Monotonically increasing sequence number per lease (LSE-5).
    /// </summary>
    [JsonPropertyName("seq")]
    public required long Seq { get; init; }
}

/// <summary>
/// Protected header of a Lease Token (typ MUST be symlease+jwt per LSE-2).
/// </summary>
public sealed record LeaseProtectedHeader(
    [property: JsonPropertyName("alg")] string Alg,
    [property: JsonPropertyName("kid")] string Kid,
    [property: JsonPropertyName("typ")] string Typ = "symlease+jwt");
