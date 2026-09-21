using System.Text.Json.Serialization;

namespace Symbolon.Crypto;

/// <summary>
/// Minimal representation of a JSON Web Key (JWK) supporting both EC (RFC 7518) and AKP (RFC 9964) keys.
/// </summary>
public sealed record JsonWebKeyDto
{
    [JsonPropertyName("kty")]
    public required string Kty { get; init; }

    [JsonPropertyName("alg")]
    public required string Alg { get; init; }

    [JsonPropertyName("kid")]
    public required string Kid { get; init; }

    [JsonPropertyName("use")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Use { get; init; }

    // EC specific (RFC 7518)
    [JsonPropertyName("crv")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Crv { get; init; }

    [JsonPropertyName("x")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? X { get; init; }

    [JsonPropertyName("y")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Y { get; init; }

    // AKP specific (RFC 9964)
    [JsonPropertyName("pub")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Pub { get; init; }
}

/// <summary>
/// JWK Set representation (RFC 7517).
/// </summary>
public sealed record JsonWebKeySetDto
{
    [JsonPropertyName("keys")]
    public required IReadOnlyList<JsonWebKeyDto> Keys { get; init; }
}
