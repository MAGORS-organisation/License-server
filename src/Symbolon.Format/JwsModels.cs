using System.Text.Json.Serialization;

namespace Symbolon.Format;

/// <summary>
/// JWS General JSON Serialization document structure according to RFC 7515 §7.2 and Symbolon LIC-3.
/// </summary>
public sealed record JwsGeneralJson(
    [property: JsonPropertyName("payload")] string Payload,
    [property: JsonPropertyName("signatures")] IReadOnlyList<JwsSignature> Signatures);

/// <summary>
/// Individual signature in JWS General JSON Serialization.
/// </summary>
public sealed record JwsSignature(
    [property: JsonPropertyName("protected")] string Protected,
    [property: JsonPropertyName("signature")] string Signature);

/// <summary>
/// Decoded JWS protected header parameters (RFC 7515, RFC 9964, Symbolon LIC-7 to LIC-11).
/// </summary>
public sealed record JwsProtectedHeader(
    [property: JsonPropertyName("alg")] string Alg,
    [property: JsonPropertyName("kid")] string Kid,
    [property: JsonPropertyName("typ")] string Typ,
    [property: JsonPropertyName("crit")] IReadOnlyList<string>? Crit = null,
    [property: JsonPropertyName("symlic")] string? Symlic = null,
    [property: JsonPropertyName("symrl")] string? Symrl = null);
