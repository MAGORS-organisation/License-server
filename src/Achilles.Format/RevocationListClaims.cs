using System.Text.Json.Serialization;

namespace Achilles.Format;

/// <summary>
/// Top-level JWT/JWS claims payload for Symbolon Revocation List (.symrl).
/// Conforms to RFC 7519 and spec/06-revocation-list.md.
/// </summary>
public sealed record RevocationListClaims(
    [property: JsonPropertyName("iss")] string Iss,
    [property: JsonPropertyName("iat")] long Iat,
    [property: JsonPropertyName("exp")] long Exp,
    [property: JsonPropertyName("symrl")] RevocationPayload Symrl);

/// <summary>
/// Payload container containing version, sequence tracking, delta markers, and revoked entities.
/// </summary>
public sealed record RevocationPayload(
    [property: JsonPropertyName("v")] int V,
    [property: JsonPropertyName("seq")] long Seq,
    [property: JsonPropertyName("full")] bool Full,
    [property: JsonPropertyName("since")] long? Since,
    [property: JsonPropertyName("revoked")] IReadOnlyList<RevocationItem> Revoked);

/// <summary>
/// Individual revocation entry. Unknown subject types ('t') MUST be ignored by verifiers (RVL-6).
/// </summary>
public sealed record RevocationItem(
    [property: JsonPropertyName("t")] string T,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("at")] long At,
    [property: JsonPropertyName("reason")] string? Reason = null)
{
    public const string TypeLicense = "license";
    public const string TypeMachine = "machine";
    public const string TypeKid = "kid";
    public const string TypeRelay = "relay";
    public const string TypeLease = "lease";

    public bool IsKnownType => T is TypeLicense or TypeMachine or TypeKid or TypeRelay or TypeLease;
}
