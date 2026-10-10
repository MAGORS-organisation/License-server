using System.Text.Json.Serialization;

namespace Achilles.Format.Attestation;

/// <summary>
/// Hardware root-of-trust or confidential computing enclave type.
/// </summary>
public enum EnclaveType
{
    None = 0,
    Tpm20 = 1,
    IntelSgx = 2,
    AmdSevSnp = 3
}

/// <summary>
/// Cryptographic quote produced by a TPM 2.0 or Confidential Computing enclave.
/// </summary>
public sealed record HardwareAttestationQuote(
    [property: JsonPropertyName("enclaveType")] EnclaveType EnclaveType,
    [property: JsonPropertyName("aikId")] string AikId,
    [property: JsonPropertyName("nonce")] string Nonce,
    [property: JsonPropertyName("pcrIndices")] IReadOnlyList<int> PcrIndices,
    [property: JsonPropertyName("pcrDigest")] string PcrDigest,
    [property: JsonPropertyName("quoteData")] string QuoteData,
    [property: JsonPropertyName("signature")] string Signature,
    [property: JsonPropertyName("signatureAlgorithm")] string SignatureAlgorithm,
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp);

/// <summary>
/// Result of attestation quote verification.
/// </summary>
public sealed record AttestationVerificationResult(
    [property: JsonPropertyName("isValid")] bool IsValid,
    [property: JsonPropertyName("failureReason")] string? FailureReason,
    [property: JsonPropertyName("quote")] HardwareAttestationQuote? Quote);
