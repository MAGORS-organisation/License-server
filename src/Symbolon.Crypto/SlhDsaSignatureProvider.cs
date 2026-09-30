using System.Buffers.Text;
using System.Security.Cryptography;

namespace Symbolon.Crypto;

/// <summary>
/// SLH-DSA (FIPS 205 / SPHINCS+) Stateless Hash-Based Digital Signature provider.
/// Provides lattice-independent quantum-resistant signatures based strictly on hash functions (SHA-2/SHAKE).
/// Serves as a backup Root of Trust in case of theoretical breaks in lattice cryptography.
/// </summary>
public sealed class SlhDsaSignatureProvider : ISignatureProvider
{
    private readonly SlhDsa? _key;
    private readonly bool _canSign;
    private readonly int _sigSize;

    public string Alg { get; }
    public string Kid { get; }
    public bool CanSign => _canSign;
    public int SignatureSize => _sigSize;

    public static bool IsSupported => SlhDsa.IsSupported;

    private SlhDsaSignatureProvider(SlhDsa? key, string kid, string alg, bool canSign, int sigSize)
    {
        _key = key;
        Kid = kid;
        Alg = alg;
        _canSign = canSign;
        _sigSize = sigSize;
    }

    public static SlhDsaSignatureProvider GenerateKey(SlhDsaAlgorithm alg, string kid)
    {
        ArgumentNullException.ThrowIfNull(alg);
        ArgumentException.ThrowIfNullOrWhiteSpace(kid);
        EnsureSupported();
        var key = SlhDsa.GenerateKey(alg);
        string algName = MapAlgorithmName(alg);
        return new SlhDsaSignatureProvider(key, kid, algName, canSign: true, key.Algorithm.SignatureSizeInBytes);
    }

    public static SlhDsaSignatureProvider ImportPublic(SlhDsaAlgorithm alg, ReadOnlySpan<byte> pubKeyBytes, string kid)
    {
        ArgumentNullException.ThrowIfNull(alg);
        ArgumentException.ThrowIfNullOrWhiteSpace(kid);
        EnsureSupported();
        var key = SlhDsa.ImportSlhDsaPublicKey(alg, pubKeyBytes);
        string algName = MapAlgorithmName(alg);
        return new SlhDsaSignatureProvider(key, kid, algName, canSign: false, key.Algorithm.SignatureSizeInBytes);
    }

    public static SlhDsaSignatureProvider ImportPrivate(SlhDsaAlgorithm alg, ReadOnlySpan<byte> privKeyBytes, string kid)
    {
        ArgumentNullException.ThrowIfNull(alg);
        ArgumentException.ThrowIfNullOrWhiteSpace(kid);
        EnsureSupported();
        var key = SlhDsa.ImportSlhDsaPrivateKey(alg, privKeyBytes);
        string algName = MapAlgorithmName(alg);
        return new SlhDsaSignatureProvider(key, kid, algName, canSign: true, key.Algorithm.SignatureSizeInBytes);
    }

    public static SlhDsaSignatureProvider ImportJwk(JsonWebKeyDto jwk)
    {
        ArgumentNullException.ThrowIfNull(jwk);
        EnsureSupported();

        if (!string.Equals(jwk.Kty, "AKP", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("JWK must have kty='AKP' for SLH-DSA.", nameof(jwk));
        }

        if (string.IsNullOrWhiteSpace(jwk.Pub))
        {
            throw new ArgumentException("JWK must contain 'pub' field with base64url encoded public key.", nameof(jwk));
        }

        var algorithm = jwk.Alg switch
        {
            Symbolon.Crypto.Alg.SlhDsaSha2128s => SlhDsaAlgorithm.SlhDsaSha2_128s,
            Symbolon.Crypto.Alg.SlhDsaSha2128f => SlhDsaAlgorithm.SlhDsaSha2_128f,
            Symbolon.Crypto.Alg.SlhDsaShake128s => SlhDsaAlgorithm.SlhDsaShake128s,
            _ => throw new NotSupportedException($"Unsupported SLH-DSA alg in JWK: {jwk.Alg}")
        };

        if (!string.IsNullOrWhiteSpace(jwk.Priv))
        {
            byte[] privBytes = Base64Url.DecodeFromChars(jwk.Priv);
            return ImportPrivate(algorithm, privBytes, jwk.Kid);
        }

        byte[] pubBytes = Base64Url.DecodeFromChars(jwk.Pub);
        return ImportPublic(algorithm, pubBytes, jwk.Kid);
    }

    public void Sign(ReadOnlySpan<byte> signingInput, Span<byte> destination)
    {
        EnsureSupported();
        if (!_canSign || _key is null)
        {
            throw new InvalidOperationException("Key does not contain private material for signing.");
        }

        if (destination.Length < SignatureSize)
        {
            throw new ArgumentException($"Destination buffer too small. Required: {SignatureSize} bytes.", nameof(destination));
        }

        _key.SignData(signingInput, destination[..SignatureSize], ReadOnlySpan<byte>.Empty);
    }

    public bool Verify(ReadOnlySpan<byte> signingInput, ReadOnlySpan<byte> signature)
    {
        EnsureSupported();
        if (_key is null) return false;
        if (signature.Length != SignatureSize) return false;

        return _key.VerifyData(signingInput, signature, ReadOnlySpan<byte>.Empty);
    }

    public JsonWebKeyDto ExportJwk(bool includePrivate = false)
    {
        EnsureSupported();
        if (_key is null) throw new InvalidOperationException("Key is null.");

        byte[] pubBytes = _key.ExportSlhDsaPublicKey();
        string pubBase64Url = Base64Url.EncodeToString(pubBytes);

        string? privBase64Url = null;
        if (includePrivate && _canSign)
        {
            byte[] privBytes = _key.ExportSlhDsaPrivateKey();
            privBase64Url = Base64Url.EncodeToString(privBytes);
        }

        return new JsonWebKeyDto
        {
            Kty = "AKP",
            Alg = Alg,
            Kid = Kid,
            Use = "sig",
            Pub = pubBase64Url,
            Priv = privBase64Url
        };
    }

    public JsonWebKeyDto ExportPublicJwk() => ExportJwk(includePrivate: false);

    public void Dispose() => _key?.Dispose();

    private static string MapAlgorithmName(SlhDsaAlgorithm alg)
    {
        if (alg == SlhDsaAlgorithm.SlhDsaSha2_128s) return Symbolon.Crypto.Alg.SlhDsaSha2128s;
        if (alg == SlhDsaAlgorithm.SlhDsaSha2_128f) return Symbolon.Crypto.Alg.SlhDsaSha2128f;
        if (alg == SlhDsaAlgorithm.SlhDsaShake128s) return Symbolon.Crypto.Alg.SlhDsaShake128s;
        return alg.Name;
    }

    private static void EnsureSupported()
    {
        if (!IsSupported)
        {
            throw new PlatformNotSupportedException(
                "FIPS 205 SLH-DSA is not currently supported by the underlying OS cryptographic subsystem on this host. " +
                "Native SLH-DSA requires Windows 11 24H2+ or Linux with OpenSSL 3.5+.");
        }
    }
}
