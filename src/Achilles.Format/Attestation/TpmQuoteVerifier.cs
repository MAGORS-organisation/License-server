using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Achilles.Crypto;

namespace Achilles.Format.Attestation;

/// <summary>
/// Verifies hardware enclave and TPM 2.0 attestation quotes according to Phase 3.0 specification.
/// Protects against replay attacks, verifies PCR register integrity, and validates license bindings.
/// </summary>
public static class TpmQuoteVerifier
{
    public const string TpmAttestMagic = "TPM2_ATTEST";

    /// <summary>
    /// Computes the canonical byte sequence for quote verification.
    /// </summary>
    public static byte[] ComputeSigningPayload(HardwareAttestationQuote quote)
    {
        ArgumentNullException.ThrowIfNull(quote);
        string sortedPcr = string.Join(",", quote.PcrIndices.OrderBy(i => i));
        string canonical = $"{TpmAttestMagic}:{quote.QuoteData}:{quote.Nonce}:{quote.PcrDigest}:{sortedPcr}";
        return Encoding.UTF8.GetBytes(canonical);
    }

    /// <summary>
    /// Cryptographically verifies a hardware enclave attestation quote.
    /// </summary>
    public static AttestationVerificationResult VerifyQuote(
        HardwareAttestationQuote quote,
        ISignatureProvider signatureVerifier,
        string expectedNonce,
        string? expectedPcrDigest = null,
        TimeSpan? maxClockSkew = null)
    {
        ArgumentNullException.ThrowIfNull(quote);
        ArgumentNullException.ThrowIfNull(signatureVerifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedNonce);

        if (quote.EnclaveType == EnclaveType.None)
        {
            return new AttestationVerificationResult(false, "Invalid enclave type: None.", quote);
        }

        // 1. Anti-replay freshness check
        if (!string.Equals(quote.Nonce, expectedNonce, StringComparison.Ordinal))
        {
            return new AttestationVerificationResult(false, "Nonce mismatch. Challenge replay detected.", quote);
        }

        // 2. PCR digest verification if expected
        if (expectedPcrDigest is not null && !string.Equals(quote.PcrDigest, expectedPcrDigest, StringComparison.OrdinalIgnoreCase))
        {
            return new AttestationVerificationResult(false, $"PCR digest mismatch. Expected: {expectedPcrDigest}, Got: {quote.PcrDigest}", quote);
        }

        // 3. Timestamp verification if maxClockSkew specified
        if (maxClockSkew.HasValue)
        {
            var diff = (DateTimeOffset.UtcNow - quote.Timestamp).Duration();
            if (diff > maxClockSkew.Value)
            {
                return new AttestationVerificationResult(false, $"Attestation quote timestamp expired (clock skew {diff.TotalSeconds}s exceeded {maxClockSkew.Value.TotalSeconds}s).", quote);
            }
        }

        // 4. Cryptographic signature check
        try
        {
            byte[] signingPayload = ComputeSigningPayload(quote);
            byte[] signatureBytes = Base64Url.DecodeFromChars(quote.Signature);

            bool signatureValid = signatureVerifier.Verify(signingPayload, signatureBytes);
            if (!signatureValid)
            {
                return new AttestationVerificationResult(false, "Cryptographic quote signature verification failed.", quote);
            }

            return new AttestationVerificationResult(true, null, quote);
        }
        catch (FormatException ex)
        {
            return new AttestationVerificationResult(false, $"Invalid signature format: {ex.Message}", quote);
        }
        catch (CryptographicException ex)
        {
            return new AttestationVerificationResult(false, $"Cryptographic error: {ex.Message}", quote);
        }
    }

    /// <summary>
    /// Validates whether an attestation quote satisfies the binding constraints in a license claim.
    /// </summary>
    public static AttestationVerificationResult ValidateLicenseBinding(
        LicenseClaims license,
        HardwareAttestationQuote quote,
        ISignatureProvider signatureVerifier,
        string expectedNonce)
    {
        ArgumentNullException.ThrowIfNull(license);
        ArgumentNullException.ThrowIfNull(quote);
        ArgumentNullException.ThrowIfNull(signatureVerifier);

        // First verify the quote cryptographically
        var quoteResult = VerifyQuote(quote, signatureVerifier, expectedNonce);
        if (!quoteResult.IsValid)
        {
            return quoteResult;
        }

        var binding = license.Symlic.Binding;
        if (binding is null)
        {
            // Unrestricted floating/perpetual license
            return quoteResult;
        }

        // Check if binding requires TPM/Enclave
        if (string.Equals(binding.Matching, "tpm20", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(binding.Matching, "enclave", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(binding.Matching, "hardware", StringComparison.OrdinalIgnoreCase))
        {
            if (binding.Components is not null)
            {
                if (binding.Components.TryGetValue("aik_id", out var expectedAik) &&
                    !string.Equals(quote.AikId, expectedAik, StringComparison.OrdinalIgnoreCase))
                {
                    return new AttestationVerificationResult(false, $"AIK ID mismatch. Expected '{expectedAik}', got '{quote.AikId}'.", quote);
                }

                if (binding.Components.TryGetValue("pcr_digest", out var expectedPcr) &&
                    !string.Equals(quote.PcrDigest, expectedPcr, StringComparison.OrdinalIgnoreCase))
                {
                    return new AttestationVerificationResult(false, $"PCR digest mismatch with license policy. Expected '{expectedPcr}', got '{quote.PcrDigest}'.", quote);
                }

                if (binding.Components.TryGetValue("enclave_type", out var expectedType) &&
                    !string.Equals(quote.EnclaveType.ToString(), expectedType, StringComparison.OrdinalIgnoreCase))
                {
                    return new AttestationVerificationResult(false, $"Enclave type mismatch. Expected '{expectedType}', got '{quote.EnclaveType}'.", quote);
                }
            }

            if (!string.IsNullOrWhiteSpace(binding.Fingerprint))
            {
                if (!string.Equals(binding.Fingerprint, quote.PcrDigest, StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(binding.Fingerprint, quote.AikId, StringComparison.OrdinalIgnoreCase))
                {
                    return new AttestationVerificationResult(false, "License binding fingerprint did not match attestation quote.", quote);
                }
            }
        }

        return new AttestationVerificationResult(true, null, quote);
    }
}
