using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using Achilles.Crypto;

namespace Achilles.Format;

/// <summary>
/// Signs Air-Gapped Grant Requests (.symreq) according to spec/07-floating-protokol.md §7.7 (FLT-32).
/// Uses Relay's identity key to create PEM-armored JWS document.
/// </summary>
public sealed class AirGapRequestSigner
{
    public const string DefaultTyp = "symreq+jws";
    public const string DefaultPemLabel = "SYMBOLON GRANT REQUEST";

    private readonly ISignatureProvider _signer;

    public AirGapRequestSigner(ISignatureProvider signer)
    {
        _signer = signer ?? throw new ArgumentNullException(nameof(signer));
    }

    /// <summary>
    /// Signs the .symreq claims and returns PEM-armored JWS string.
    /// </summary>
    public string Sign(AirGapRequestClaims claims, bool pemArmor = true, string pemLabel = DefaultPemLabel)
    {
        ArgumentNullException.ThrowIfNull(claims);

        byte[] payloadJson = JsonSerializer.SerializeToUtf8Bytes(claims, AchillesJsonContext.Default.AirGapRequestClaims);
        string payloadB64 = Base64Url.EncodeToString(payloadJson);

        var header = new JwsProtectedHeader(
            Alg: _signer.Alg,
            Kid: _signer.Kid,
            Typ: DefaultTyp,
            Crit: ["symreq"],
            Symlic: "1");

        byte[] headerBytes = JsonSerializer.SerializeToUtf8Bytes(header, AchillesJsonContext.Default.JwsProtectedHeader);
        string headerB64 = Base64Url.EncodeToString(headerBytes);

        string signingInputStr = $"{headerB64}.{payloadB64}";
        byte[] signingInputBytes = Encoding.ASCII.GetBytes(signingInputStr);

        byte[] sigBytes = new byte[_signer.SignatureSize];
        _signer.Sign(signingInputBytes, sigBytes);

        var doc = new JwsGeneralJson(
            Payload: payloadB64,
            Signatures:
            [
                new JwsSignature(
                    Protected: headerB64,
                    Signature: Base64Url.EncodeToString(sigBytes))
            ]);

        byte[] docBytes = JsonSerializer.SerializeToUtf8Bytes(doc, AchillesJsonContext.Default.JwsGeneralJson);
        return pemArmor ? PemArmor.Wrap(pemLabel, docBytes) : Encoding.UTF8.GetString(docBytes);
    }
}
