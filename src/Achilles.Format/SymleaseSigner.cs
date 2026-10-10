using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using Achilles.Crypto;

namespace Achilles.Format;

/// <summary>
/// Signs Symlease Claims into a multi-signature JWS General JSON document wrapped in PEM armor.
/// Conforms to RFC 7515 §7.2, RFC 9964, and Symbolon spec/07-floating-protokol.md §7.4 (FLT-19).
/// </summary>
public sealed class SymleaseSigner
{
    public const string DefaultTyp = "symlease+jws";
    public const string DefaultPemLabel = "SYMBOLON LEASE";

    private readonly IReadOnlyList<ISignatureProvider> _signers;

    public SymleaseSigner(IReadOnlyList<ISignatureProvider> signers)
    {
        ArgumentNullException.ThrowIfNull(signers);
        if (signers.Count == 0)
        {
            throw new ArgumentException("At least one signature provider must be specified (FLT-19).", nameof(signers));
        }

        var seenAlgs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in signers)
        {
            if (!seenAlgs.Add(s.Alg))
            {
                throw new ArgumentException($"Duplicate signature algorithm '{s.Alg}' is forbidden.", nameof(signers));
            }
        }

        _signers = signers;
    }

    /// <summary>
    /// Signs claims and returns PEM-armored JWS document or bare JWS document.
    /// </summary>
    public string Sign(SymleaseClaims claims, bool pemArmor = true, string pemLabel = DefaultPemLabel)
    {
        ArgumentNullException.ThrowIfNull(claims);

        byte[] payloadJson = JsonSerializer.SerializeToUtf8Bytes(claims, AchillesJsonContext.Default.SymleaseClaims);
        string payloadB64 = Base64Url.EncodeToString(payloadJson);

        var signatures = new List<JwsSignature>(_signers.Count);
        foreach (var signer in _signers)
        {
            var header = new JwsProtectedHeader(
                Alg: signer.Alg,
                Kid: signer.Kid,
                Typ: DefaultTyp,
                Crit: ["symlease"],
                Symlic: "1");

            byte[] headerBytes = JsonSerializer.SerializeToUtf8Bytes(header, AchillesJsonContext.Default.JwsProtectedHeader);
            string headerB64 = Base64Url.EncodeToString(headerBytes);

            string signingInputStr = $"{headerB64}.{payloadB64}";
            byte[] signingInputBytes = Encoding.ASCII.GetBytes(signingInputStr);

            byte[] sigBytes = new byte[signer.SignatureSize];
            signer.Sign(signingInputBytes, sigBytes);

            signatures.Add(new JwsSignature(
                Protected: headerB64,
                Signature: Base64Url.EncodeToString(sigBytes)));
        }

        var doc = new JwsGeneralJson(
            Payload: payloadB64,
            Signatures: signatures);

        byte[] docBytes = JsonSerializer.SerializeToUtf8Bytes(doc, AchillesJsonContext.Default.JwsGeneralJson);
        return pemArmor ? PemArmor.Wrap(pemLabel, docBytes) : Encoding.UTF8.GetString(docBytes);
    }
}
