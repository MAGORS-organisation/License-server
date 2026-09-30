namespace Symbolon.Crypto;

/// <summary>
/// Evaluates and enforces Post-Quantum cryptographic profiles (hybrid-v1 vs pqc-strict).
/// Ensures strict adherence to US CNSA 2.0 and EU NIS 2 PQC 2030 regulations.
/// </summary>
public static class PqcProfileValidator
{
    /// <summary>
    /// Validates whether a cryptographic algorithm is permitted under the specified security profile.
    /// In 'pqc-strict' mode, classical algorithms (ES256, RSA) are strictly rejected.
    /// </summary>
    public static bool IsAlgorithmPermitted(string alg, string profile)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(alg);

        if (string.Equals(profile, PqcProfiles.PqcStrict, StringComparison.OrdinalIgnoreCase))
        {
            // Zero classical cryptography: only post-quantum algorithms permitted
            return Alg.IsPostQuantum(alg);
        }

        // hybrid-v1 or default: allows ES256 and post-quantum algorithms
        return alg is Alg.Es256 || Alg.IsPostQuantum(alg);
    }

    /// <summary>
    /// Validates whether a JWK complies with the required profile.
    /// </summary>
    public static bool IsKeyPermitted(JsonWebKeyDto jwk, string profile)
    {
        ArgumentNullException.ThrowIfNull(jwk);
        return IsAlgorithmPermitted(jwk.Alg, profile);
    }

    /// <summary>
    /// Checks CNSA 2.0 compliance for national security / enterprise deployments.
    /// CNSA 2.0 mandates ML-KEM-768/1024 for key exchange and ML-DSA-65/87 or SLH-DSA for signatures.
    /// </summary>
    public static bool IsCnsa2Compliant(string alg)
    {
        return alg is Alg.MlDsa65 or Alg.MlDsa87
            or Alg.MlKem768 or Alg.MlKem1024
            or Alg.SlhDsaSha2128s or Alg.SlhDsaShake128s;
    }
}
