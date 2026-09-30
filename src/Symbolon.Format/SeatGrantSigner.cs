using System.Buffers.Text;
using System.Text;
using System.Text.Json;
using Symbolon.Crypto;

namespace Symbolon.Format;

/// <summary>
/// Signs Seat Grant Claims into a multi-signature JWS General JSON document wrapped in PEM armor.
/// Conforms to RFC 7515 §7.2, RFC 9964, and Symbolon spec/05-seat-grant.md (GNT-1, GNT-2, GNT-3).
/// </summary>
public sealed class SeatGrantSigner
{
    public const string DefaultTyp = "symgrant+jws";
    public const string DefaultPemLabel = "SYMBOLON SEAT GRANT";

    private readonly IReadOnlyList<ISignatureProvider> _signers;

    public SeatGrantSigner(IReadOnlyList<ISignatureProvider> signers)
    {
        ArgumentNullException.ThrowIfNull(signers);
        if (signers.Count == 0)
        {
            throw new ArgumentException("At least one signature provider must be specified (GNT-2).", nameof(signers));
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
    /// Signs seat grant claims and returns PEM-armored JWS document or bare JWS JSON.
    /// Validates GNT-3 seatRange invariant prior to signing.
    /// </summary>
    public string Sign(SeatGrantDocumentClaims claims, bool pemArmor = true, string pemLabel = DefaultPemLabel)
    {
        ArgumentNullException.ThrowIfNull(claims);

        // GNT-3: seatRange[1] - seatRange[0] + 1 == seats
        if (claims.Symgrant.SeatRange is null || claims.Symgrant.SeatRange.Count != 2)
        {
            throw new ArgumentException("SeatRange must contain exactly 2 numbers [from, to] (GNT-3).", nameof(claims));
        }

        int rangeCount = claims.Symgrant.SeatRange[1] - claims.Symgrant.SeatRange[0] + 1;
        if (rangeCount != claims.Symgrant.Seats)
        {
            throw new ArgumentException(
                $"SeatRange [{claims.Symgrant.SeatRange[0]}, {claims.Symgrant.SeatRange[1]}] span ({rangeCount}) must equal seats count ({claims.Symgrant.Seats}) (GNT-3).",
                nameof(claims));
        }

        byte[] payloadJson = JsonSerializer.SerializeToUtf8Bytes(claims, SymbolonJsonContext.Default.SeatGrantDocumentClaims);
        string payloadB64 = Base64Url.EncodeToString(payloadJson);

        var signatures = new List<JwsSignature>(_signers.Count);
        foreach (var signer in _signers)
        {
            var header = new JwsProtectedHeader(
                Alg: signer.Alg,
                Kid: signer.Kid,
                Typ: DefaultTyp,
                Crit: ["symgrant"],
                Symlic: "1");

            byte[] headerBytes = JsonSerializer.SerializeToUtf8Bytes(header, SymbolonJsonContext.Default.JwsProtectedHeader);
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

        byte[] docBytes = JsonSerializer.SerializeToUtf8Bytes(doc, SymbolonJsonContext.Default.JwsGeneralJson);
        return pemArmor ? PemArmor.Wrap(pemLabel, docBytes) : Encoding.UTF8.GetString(docBytes);
    }
}
