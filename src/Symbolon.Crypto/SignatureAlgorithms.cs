namespace Symbolon.Crypto;

/// <summary>
/// JOSE "alg" identifier constants according to RFC 7518 and RFC 9964 (ML-DSA for JOSE/COSE).
/// </summary>
public static class Alg
{
    public const string Es256 = "ES256";
    public const string MlDsa44 = "ML-DSA-44";
    public const string MlDsa65 = "ML-DSA-65";
    public const string MlDsa87 = "ML-DSA-87";
}
