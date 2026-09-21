using System.Buffers.Text;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Symbolon.Crypto;

namespace Symbolon.Format;

/// <summary>
/// Signs License Claims into a multi-signature JWS General JSON document wrapped in PEM armor.
/// Conforms to RFC 7515 §7.2, RFC 9964, and Symbolon spec/03-symlic-1.md.
/// </summary>
public sealed class LicenseDocumentSigner
{
    private readonly IReadOnlyList<ISignatureProvider> _signers;

    public LicenseDocumentSigner(IReadOnlyList<ISignatureProvider> signers)
    {
        ArgumentNullException.ThrowIfNull(signers);
        if (signers.Count == 0)
        {
            throw new ArgumentException("At least one signature provider must be specified (LIC-6).", nameof(signers));
        }

        // LIC-11: An issuer MUST NOT put two signatures with the same alg into a single document
        var seenAlgs = new HashSet<string>(StringComparer.Ordinal);
        foreach (var s in signers)
        {
            if (!seenAlgs.Add(s.Alg))
            {
                throw new ArgumentException($"Duplicate signature algorithm '{s.Alg}' is forbidden (LIC-11).", nameof(signers));
            }
        }

        _signers = signers;
    }

    /// <summary>
    /// Signs claims and returns PEM-armored JWS document.
    /// </summary>
    public string Sign(LicenseClaims claims, string typ = "symlic+jws", string pemLabel = "SYMBOLON LICENSE")
    {
        ArgumentNullException.ThrowIfNull(claims);

        byte[] payloadJson = JsonSerializer.SerializeToUtf8Bytes(claims, SymbolonJsonContext.Default.LicenseClaims);
        string payloadB64 = Base64Url.EncodeToString(payloadJson);

        var signatures = new List<JwsSignature>(_signers.Count);
        foreach (var signer in _signers)
        {
            var header = new JwsProtectedHeader(
                Alg: signer.Alg,
                Kid: signer.Kid,
                Typ: typ,
                Crit: ["symlic"],
                Symlic: claims.Symlic.V.ToString(CultureInfo.InvariantCulture));

            byte[] headerBytes = JsonSerializer.SerializeToUtf8Bytes(header, SymbolonJsonContext.Default.JwsProtectedHeader);
            string protectedB64 = Base64Url.EncodeToString(headerBytes);

            // RFC 7515 §5.2: ASCII(BASE64URL(protected) || '.' || BASE64URL(payload))
            byte[] signingInput = Encoding.ASCII.GetBytes($"{protectedB64}.{payloadB64}");

            byte[] signatureBytes = new byte[signer.SignatureSize];
            signer.Sign(signingInput, signatureBytes);

            signatures.Add(new JwsSignature(protectedB64, Base64Url.EncodeToString(signatureBytes)));
        }

        var jwsDoc = new JwsGeneralJson(payloadB64, signatures);

        byte[] docBytes = JsonSerializer.SerializeToUtf8Bytes(jwsDoc, SymbolonJsonContext.Default.JwsGeneralJson);
        return PemArmor.Wrap(pemLabel, docBytes);
    }
}
