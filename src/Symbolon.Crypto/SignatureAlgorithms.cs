namespace Symbolon.Crypto;

/// <summary>
/// JOSE "alg" identifier constants according to RFC 7518, RFC 9964 (ML-DSA for JOSE/COSE),
/// FIPS 203 (ML-KEM) and FIPS 205 (SLH-DSA).
/// </summary>
public static class Alg
{
    public const string Es256 = "ES256";
    public const string MlDsa44 = "ML-DSA-44";
    public const string MlDsa65 = "ML-DSA-65";
    public const string MlDsa87 = "ML-DSA-87";

    // FIPS 203: ML-KEM (Key Encapsulation Mechanism)
    public const string MlKem512 = "ML-KEM-512";
    public const string MlKem768 = "ML-KEM-768";
    public const string MlKem1024 = "ML-KEM-1024";

    // FIPS 205: SLH-DSA (Stateless Hash-Based Digital Signatures / SPHINCS+)
    public const string SlhDsaSha2128s = "SLH-DSA-SHA2-128s";
    public const string SlhDsaSha2128f = "SLH-DSA-SHA2-128f";
    public const string SlhDsaShake128s = "SLH-DSA-SHAKE-128s";

    public static bool IsPostQuantum(string? alg) =>
        alg is MlDsa44 or MlDsa65 or MlDsa87
            or MlKem512 or MlKem768 or MlKem1024
            or SlhDsaSha2128s or SlhDsaSha2128f or SlhDsaShake128s;

    public static bool IsSignatureAlgorithm(string? alg) =>
        alg is Es256 or MlDsa44 or MlDsa65 or MlDsa87
            or SlhDsaSha2128s or SlhDsaSha2128f or SlhDsaShake128s;

    public static bool IsKeyEncapsulation(string? alg) =>
        alg is MlKem512 or MlKem768 or MlKem1024;
}

/// <summary>
/// Cryptographic security profiles for Symbolon license server and artifacts.
/// </summary>
public static class PqcProfiles
{
    /// <summary>
    /// Dual hybrid mode: Classical ES256 + ML-DSA-65 post-quantum signature.
    /// Provides backwards compatibility while defending against quantum adversaries.
    /// </summary>
    public const string HybridV1 = "hybrid-v1";

    /// <summary>
    /// Strict Post-Quantum mode (Zero Classical Cryptography).
    /// Pure ML-DSA / SLH-DSA signatures and ML-KEM key encapsulation.
    /// Meets US CNSA 2.0 and EU NIS 2 PQC 2030 compliance mandates.
    /// </summary>
    public const string PqcStrict = "pqc-strict";

    public static bool IsValidProfile(string? profile) =>
        string.Equals(profile, HybridV1, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(profile, PqcStrict, StringComparison.OrdinalIgnoreCase);
}

