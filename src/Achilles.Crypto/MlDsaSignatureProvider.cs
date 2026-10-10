using System.Buffers.Text;
using System.Security.Cryptography;

namespace Achilles.Crypto;

/// <summary>
/// ML-DSA (FIPS 204 / RFC 9964) signature provider using .NET 10 native crypto APIs.
/// </summary>
public sealed class MlDsaSignatureProvider : ISignatureProvider
{
    private readonly MLDsa _key;
    private readonly bool _canSign;

    public string Alg { get; }
    public string Kid { get; }
    public bool CanSign => _canSign;
    public int SignatureSize { get; }

    public static bool IsSupported => MLDsa.IsSupported;

    private MlDsaSignatureProvider(MLDsa key, string kid, bool canSign)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(kid);

        _key = key;
        Kid = kid;
        _canSign = canSign;
        Alg = key.Algorithm.Name switch
        {
            "ML-DSA-44" => Achilles.Crypto.Alg.MlDsa44,
            "ML-DSA-65" => Achilles.Crypto.Alg.MlDsa65,
            "ML-DSA-87" => Achilles.Crypto.Alg.MlDsa87,
            var name => throw new NotSupportedException($"Unsupported ML-DSA parameter set: {name}")
        };
        SignatureSize = key.Algorithm.SignatureSizeInBytes;
    }

    public static MlDsaSignatureProvider GenerateKey(MLDsaAlgorithm alg, string kid)
    {
        EnsureSupported();
        return new MlDsaSignatureProvider(MLDsa.GenerateKey(alg), kid, canSign: true);
    }

    /// <summary>
    /// Imports an ML-DSA private key from its 32-byte seed according to FIPS 204 and RFC 9964.
    /// </summary>
    public static MlDsaSignatureProvider ImportSeed(MLDsaAlgorithm alg, ReadOnlySpan<byte> seed, string kid)
    {
        EnsureSupported();
        return new MlDsaSignatureProvider(MLDsa.ImportMLDsaPrivateSeed(alg, seed), kid, canSign: true);
    }

    public static MlDsaSignatureProvider ImportPublic(MLDsaAlgorithm alg, ReadOnlySpan<byte> pubKeyBytes, string kid)
    {
        EnsureSupported();
        return new MlDsaSignatureProvider(MLDsa.ImportMLDsaPublicKey(alg, pubKeyBytes), kid, canSign: false);
    }

    public static MlDsaSignatureProvider ImportJwk(JsonWebKeyDto jwk)
    {
        EnsureSupported();
        ArgumentNullException.ThrowIfNull(jwk);

        if (!string.Equals(jwk.Kty, "AKP", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("JWK must have kty='AKP' for ML-DSA (RFC 9964).", nameof(jwk));
        }

        if (string.IsNullOrWhiteSpace(jwk.Pub))
        {
            throw new ArgumentException("JWK must contain 'pub' field with base64url encoded public key.", nameof(jwk));
        }

        var algorithm = jwk.Alg switch
        {
            Achilles.Crypto.Alg.MlDsa44 => MLDsaAlgorithm.MLDsa44,
            Achilles.Crypto.Alg.MlDsa65 => MLDsaAlgorithm.MLDsa65,
            Achilles.Crypto.Alg.MlDsa87 => MLDsaAlgorithm.MLDsa87,
            _ => throw new NotSupportedException($"Unsupported ML-DSA alg in JWK: {jwk.Alg}")
        };

        byte[] pubBytes = Base64Url.DecodeFromChars(jwk.Pub);
        return ImportPublic(algorithm, pubBytes, jwk.Kid);
    }

    public void Sign(ReadOnlySpan<byte> signingInput, Span<byte> destination)
    {
        if (!_canSign)
        {
            throw new InvalidOperationException("Key does not contain private material for signing.");
        }

        if (destination.Length < SignatureSize)
        {
            throw new ArgumentException($"Destination buffer too small. Required: {SignatureSize} bytes.", nameof(destination));
        }

        // Context is empty per RFC 9964 (ML-DSA for JOSE/COSE does not use a context string).
        _key.SignData(signingInput, destination[..SignatureSize], ReadOnlySpan<byte>.Empty);
    }

    public bool Verify(ReadOnlySpan<byte> signingInput, ReadOnlySpan<byte> signature)
    {
        if (signature.Length != SignatureSize)
        {
            return false;
        }

        return _key.VerifyData(signingInput, signature, ReadOnlySpan<byte>.Empty);
    }

    public JsonWebKeyDto ExportPublicJwk()
    {
        return new JsonWebKeyDto
        {
            Kty = "AKP", // RFC 9964 Algorithm Key Pair
            Alg = Alg,
            Kid = Kid,
            Use = "sig",
            Pub = Base64Url.EncodeToString(_key.ExportMLDsaPublicKey())
        };
    }

    private static void EnsureSupported()
    {
        if (!MLDsa.IsSupported)
        {
            throw new PlatformNotSupportedException(
                "ML-DSA is not supported on this platform. Requires Windows CNG PQC or Linux with OpenSSL 3.5+.");
        }
    }

    public void Dispose() => _key.Dispose();
}
