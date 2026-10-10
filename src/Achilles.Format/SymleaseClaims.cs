using System.Text.Json.Serialization;

namespace Achilles.Format;

/// <summary>
/// Top-level JWT/JWS claims payload for Symbolon Offline Roaming Borrow Lease (.symlease).
/// Conforms to RFC 7519, RFC 7515, and Symbolon spec/07-floating-protokol.md §7.4 (FLT-17..FLT-22).
/// </summary>
public sealed record SymleaseClaims(
    [property: JsonPropertyName("iss")] string Iss,
    [property: JsonPropertyName("sub")] string Sub,
    [property: JsonPropertyName("jti")] string Jti,
    [property: JsonPropertyName("iat")] long Iat,
    [property: JsonPropertyName("nbf")] long Nbf,
    [property: JsonPropertyName("exp")] long Exp,
    [property: JsonPropertyName("seat")] int Seat,
    [property: JsonPropertyName("fp")] string Fp,
    [property: JsonPropertyName("ent")] IReadOnlyList<string> Ent,
    [property: JsonPropertyName("borrow")] SymleaseBorrowPayload Borrow);

/// <summary>
/// Metadata payload for offline roaming borrow details.
/// </summary>
public sealed record SymleaseBorrowPayload(
    [property: JsonPropertyName("days")] int Days,
    [property: JsonPropertyName("borrowedAt")] long BorrowedAt,
    [property: JsonPropertyName("borrowedUntil")] long BorrowedUntil,
    [property: JsonPropertyName("possessionKeyJwk")] string PossessionKeyJwk);
