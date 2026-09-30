using System.Buffers.Text;
using System.Security.Cryptography;

namespace Symbolon.Crypto;

/// <summary>
/// ML-KEM (FIPS 203) Key Encapsulation Mechanism provider using .NET 10 native crypto APIs.
/// Defends against Harvest-Now-Decrypt-Later (HNDL) attacks by establishing post-quantum shared secrets.
/// </summary>
public sealed class MlKemKeyEncapsulationProvider : IKeyEncapsulationProvider
{
    private readonly MLKem _key;
    private readonly bool _canDecapsulate;
    private bool _disposed;

    public string Alg { get; }
    public string Kid { get; }
    public bool CanDecapsulate => _canDecapsulate;
    public int CiphertextSize { get; }
    public int SharedSecretSize { get; }

    public static bool IsSupported => MLKem.IsSupported;

    private MlKemKeyEncapsulationProvider(MLKem key, string kid, bool canDecapsulate)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(kid);

        _key = key;
        Kid = kid;
        _canDecapsulate = canDecapsulate;
        Alg = key.Algorithm.Name switch
        {
            "ML-KEM-512" => Symbolon.Crypto.Alg.MlKem512,
            "ML-KEM-768" => Symbolon.Crypto.Alg.MlKem768,
            "ML-KEM-1024" => Symbolon.Crypto.Alg.MlKem1024,
            var name => throw new NotSupportedException($"Unsupported ML-KEM parameter set: {name}")
        };
        CiphertextSize = key.Algorithm.CiphertextSizeInBytes;
        SharedSecretSize = key.Algorithm.SharedSecretSizeInBytes;
    }

    public static MlKemKeyEncapsulationProvider GenerateKey(MLKemAlgorithm alg, string kid)
    {
        ArgumentNullException.ThrowIfNull(alg);
        ArgumentException.ThrowIfNullOrWhiteSpace(kid);
        EnsureSupported();
        return new MlKemKeyEncapsulationProvider(MLKem.GenerateKey(alg), kid, canDecapsulate: true);
    }

    public static MlKemKeyEncapsulationProvider ImportSeed(MLKemAlgorithm alg, ReadOnlySpan<byte> seed, string kid)
    {
        ArgumentNullException.ThrowIfNull(alg);
        ArgumentException.ThrowIfNullOrWhiteSpace(kid);
        EnsureSupported();
        return new MlKemKeyEncapsulationProvider(MLKem.ImportPrivateSeed(alg, seed), kid, canDecapsulate: true);
    }

    public static MlKemKeyEncapsulationProvider ImportEncapsulationKey(MLKemAlgorithm alg, ReadOnlySpan<byte> pubKeyBytes, string kid)
    {
        ArgumentNullException.ThrowIfNull(alg);
        ArgumentException.ThrowIfNullOrWhiteSpace(kid);
        EnsureSupported();
        return new MlKemKeyEncapsulationProvider(MLKem.ImportEncapsulationKey(alg, pubKeyBytes), kid, canDecapsulate: false);
    }

    public static MlKemKeyEncapsulationProvider ImportDecapsulationKey(MLKemAlgorithm alg, ReadOnlySpan<byte> privKeyBytes, string kid)
    {
        ArgumentNullException.ThrowIfNull(alg);
        ArgumentException.ThrowIfNullOrWhiteSpace(kid);
        EnsureSupported();
        return new MlKemKeyEncapsulationProvider(MLKem.ImportDecapsulationKey(alg, privKeyBytes), kid, canDecapsulate: true);
    }

    public static MlKemKeyEncapsulationProvider ImportJwk(JsonWebKeyDto jwk)
    {
        EnsureSupported();
        ArgumentNullException.ThrowIfNull(jwk);

        if (!string.Equals(jwk.Kty, "AKP", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("JWK must have kty='AKP' for ML-KEM.", nameof(jwk));
        }

        if (string.IsNullOrWhiteSpace(jwk.Pub))
        {
            throw new ArgumentException("JWK must contain 'pub' field with base64url encoded encapsulation key.", nameof(jwk));
        }

        var algorithm = jwk.Alg switch
        {
            Symbolon.Crypto.Alg.MlKem512 => MLKemAlgorithm.MLKem512,
            Symbolon.Crypto.Alg.MlKem768 => MLKemAlgorithm.MLKem768,
            Symbolon.Crypto.Alg.MlKem1024 => MLKemAlgorithm.MLKem1024,
            _ => throw new NotSupportedException($"Unsupported ML-KEM alg in JWK: {jwk.Alg}")
        };

        if (!string.IsNullOrWhiteSpace(jwk.Priv))
        {
            byte[] privBytes = Base64Url.DecodeFromChars(jwk.Priv);
            if (privBytes.Length == algorithm.PrivateSeedSizeInBytes)
            {
                return ImportSeed(algorithm, privBytes, jwk.Kid);
            }
            return ImportDecapsulationKey(algorithm, privBytes, jwk.Kid);
        }

        byte[] pubBytes = Base64Url.DecodeFromChars(jwk.Pub);
        return ImportEncapsulationKey(algorithm, pubBytes, jwk.Kid);
    }

    public (byte[] Ciphertext, byte[] SharedSecret) Encapsulate()
    {
        ThrowIfDisposed();
        byte[] ct = new byte[CiphertextSize];
        byte[] ss = new byte[SharedSecretSize];
        _key.Encapsulate(ct, ss);
        return (ct, ss);
    }

    public void Encapsulate(Span<byte> ciphertext, Span<byte> sharedSecret)
    {
        ThrowIfDisposed();
        if (ciphertext.Length < CiphertextSize)
        {
            throw new ArgumentException($"Ciphertext buffer too small. Required: {CiphertextSize} bytes.", nameof(ciphertext));
        }
        if (sharedSecret.Length < SharedSecretSize)
        {
            throw new ArgumentException($"SharedSecret buffer too small. Required: {SharedSecretSize} bytes.", nameof(sharedSecret));
        }

        _key.Encapsulate(ciphertext[..CiphertextSize], sharedSecret[..SharedSecretSize]);
    }

    public byte[] Decapsulate(ReadOnlySpan<byte> ciphertext)
    {
        ThrowIfDisposed();
        if (!_canDecapsulate)
        {
            throw new InvalidOperationException("Key does not contain decapsulation material.");
        }

        byte[] ss = new byte[SharedSecretSize];
        _key.Decapsulate(ciphertext, ss);
        return ss;
    }

    public void Decapsulate(ReadOnlySpan<byte> ciphertext, Span<byte> destination)
    {
        ThrowIfDisposed();
        if (!_canDecapsulate)
        {
            throw new InvalidOperationException("Key does not contain decapsulation material.");
        }
        if (destination.Length < SharedSecretSize)
        {
            throw new ArgumentException($"Destination buffer too small. Required: {SharedSecretSize} bytes.", nameof(destination));
        }

        _key.Decapsulate(ciphertext, destination[..SharedSecretSize]);
    }

    public JsonWebKeyDto ExportJwk(bool includePrivate = false)
    {
        ThrowIfDisposed();
        byte[] pubBytes = _key.ExportEncapsulationKey();
        string pubBase64Url = Base64Url.EncodeToString(pubBytes);

        string? privBase64Url = null;
        if (includePrivate && _canDecapsulate)
        {
            byte[] privBytes = _key.ExportPrivateSeed();
            privBase64Url = Base64Url.EncodeToString(privBytes);
        }

        return new JsonWebKeyDto
        {
            Kty = "AKP",
            Alg = Alg,
            Kid = Kid,
            Use = "enc",
            Pub = pubBase64Url,
            Priv = privBase64Url
        };
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            _key.Dispose();
            _disposed = true;
        }
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    private static void EnsureSupported()
    {
        if (!IsSupported)
        {
            throw new PlatformNotSupportedException("ML-KEM is not supported on this platform.");
        }
    }
}
