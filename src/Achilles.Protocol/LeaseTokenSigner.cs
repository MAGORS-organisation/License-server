using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using Achilles.Crypto;

namespace Achilles.Protocol;

/// <summary>
/// Issues compact JWS Lease Tokens according to spec/04-lease-token.md (LSE-1 to LSE-10).
/// </summary>
public sealed class LeaseTokenSigner
{
    private readonly ISignatureProvider _signer;

    public LeaseTokenSigner(ISignatureProvider signer)
    {
        ArgumentNullException.ThrowIfNull(signer);
        if (!signer.CanSign)
        {
            throw new ArgumentException("Signer must have private key material.", nameof(signer));
        }

        _signer = signer;
    }

    /// <summary>
    /// Signs LeaseClaims into a compact JWS token (BASE64URL(protected).BASE64URL(payload).BASE64URL(signature)).
    /// </summary>
    public string IssueToken(LeaseClaims claims)
    {
        ArgumentNullException.ThrowIfNull(claims);

        var header = new LeaseProtectedHeader(
            Alg: _signer.Alg,
            Kid: _signer.Kid,
            Typ: "symlease+jwt");

        byte[] headerJson = JsonSerializer.SerializeToUtf8Bytes(header, AchillesProtocolJsonContext.Default.LeaseProtectedHeader);
        string headerB64 = Base64Url.EncodeToString(headerJson);

        byte[] payloadJson = JsonSerializer.SerializeToUtf8Bytes(claims, AchillesProtocolJsonContext.Default.LeaseClaims);
        string payloadB64 = Base64Url.EncodeToString(payloadJson);

        byte[] signingInput = Encoding.ASCII.GetBytes($"{headerB64}.{payloadB64}");
        byte[] signature = new byte[_signer.SignatureSize];
        _signer.Sign(signingInput, signature);

        string signatureB64 = Base64Url.EncodeToString(signature);

        return $"{headerB64}.{payloadB64}.{signatureB64}";
    }
}
