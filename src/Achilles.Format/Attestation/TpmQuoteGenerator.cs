using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Achilles.Crypto;

namespace Achilles.Format.Attestation;

/// <summary>
/// Generates cryptographically signed TPM 2.0 and enclave attestation quotes.
/// </summary>
public static class TpmQuoteGenerator
{
    public static HardwareAttestationQuote CreateQuote(
        ISignatureProvider aikSigner,
        EnclaveType enclaveType,
        string nonce,
        IReadOnlyList<int> pcrIndices,
        string? customPcrDigest = null,
        DateTimeOffset? timestamp = null)
    {
        ArgumentNullException.ThrowIfNull(aikSigner);
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);
        ArgumentNullException.ThrowIfNull(pcrIndices);

        var ts = timestamp ?? DateTimeOffset.UtcNow;
        string pcrDigest = customPcrDigest ?? ComputePcrCompositeDigest(pcrIndices);
        string quoteData = $"TPMS_ATTEST_v2_{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(nonce)))[..16]}";

        var dummyQuote = new HardwareAttestationQuote(
            EnclaveType: enclaveType,
            AikId: aikSigner.Kid,
            Nonce: nonce,
            PcrIndices: pcrIndices,
            PcrDigest: pcrDigest,
            QuoteData: quoteData,
            Signature: string.Empty,
            SignatureAlgorithm: aikSigner.Alg,
            Timestamp: ts);

        byte[] payload = TpmQuoteVerifier.ComputeSigningPayload(dummyQuote);
        byte[] sigBytes = new byte[aikSigner.SignatureSize];
        aikSigner.Sign(payload, sigBytes);

        return dummyQuote with
        {
            Signature = Base64Url.EncodeToString(sigBytes)
        };
    }

    public static string ComputePcrCompositeDigest(IEnumerable<int> pcrIndices)
    {
        ArgumentNullException.ThrowIfNull(pcrIndices);
        var sorted = pcrIndices.OrderBy(x => x);
        byte[] input = Encoding.UTF8.GetBytes($"PCR_COMPOSITE:{string.Join(":", sorted)}");
        return $"sha256:{Convert.ToHexStringLower(SHA256.HashData(input))}";
    }
}
