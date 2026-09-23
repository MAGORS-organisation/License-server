using System.Buffers.Text;
using System.Globalization;
using System.Text;
using System.Text.Json;
using Symbolon.Crypto;

namespace Symbolon.Format;

/// <summary>
/// Signs Revocation List Claims into a multi-signature JWS General JSON document wrapped in PEM armor.
/// Conforms to RFC 7515 §7.2, RFC 9964, and Symbolon spec/06-revocation-list.md (RVL-1, RVL-2, RVL-3).
/// </summary>
public sealed class RevocationListSigner
{
    public const string DefaultTyp = "symrl+jws";
    public const string DefaultPemLabel = "SYMBOLON REVOCATION LIST";

    private readonly IReadOnlyList<ISignatureProvider> _signers;

    public RevocationListSigner(IReadOnlyList<ISignatureProvider> signers)
    {
        ArgumentNullException.ThrowIfNull(signers);
        if (signers.Count == 0)
        {
            throw new ArgumentException("At least one signature provider must be specified (RVL-2).", nameof(signers));
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
    public string Sign(RevocationListClaims claims, bool pemArmor = true, string pemLabel = DefaultPemLabel)
    {
        ArgumentNullException.ThrowIfNull(claims);

        byte[] payloadJson = JsonSerializer.SerializeToUtf8Bytes(claims, SymbolonJsonContext.Default.RevocationListClaims);
        string payloadB64 = Base64Url.EncodeToString(payloadJson);

        var signatures = new List<JwsSignature>(_signers.Count);
        foreach (var signer in _signers)
        {
            var header = new JwsProtectedHeader(
                Alg: signer.Alg,
                Kid: signer.Kid,
                Typ: DefaultTyp,
                Crit: ["symrl"],
                Symrl: claims.Symrl.V.ToString(CultureInfo.InvariantCulture));

            byte[] headerBytes = JsonSerializer.SerializeToUtf8Bytes(header, SymbolonJsonContext.Default.JwsProtectedHeader);
            string protectedB64 = Base64Url.EncodeToString(headerBytes);

            byte[] signingInput = Encoding.ASCII.GetBytes($"{protectedB64}.{payloadB64}");

            byte[] signatureBytes = new byte[signer.SignatureSize];
            signer.Sign(signingInput, signatureBytes);

            signatures.Add(new JwsSignature(protectedB64, Base64Url.EncodeToString(signatureBytes)));
        }

        var jwsDoc = new JwsGeneralJson(payloadB64, signatures);
        byte[] docBytes = JsonSerializer.SerializeToUtf8Bytes(jwsDoc, SymbolonJsonContext.Default.JwsGeneralJson);

        return pemArmor ? PemArmor.Wrap(pemLabel, docBytes) : Encoding.UTF8.GetString(docBytes);
    }
}
