using System.Buffers.Text;
using System.Security.Cryptography;

namespace Achilles.Crypto;

/// <summary>
/// ECDSA P-256 with SHA-256 signature provider (ES256, RFC 7518) using IEEE P1363 signature format.
/// </summary>
public sealed class Es256SignatureProvider : ISignatureProvider
{
    private readonly ECDsa _key;
    private readonly bool _canSign;

    public string Alg => Achilles.Crypto.Alg.Es256;
    public string Kid { get; }
    public bool CanSign => _canSign;
    public int SignatureSize => 64; // P-256, IEEE P1363 r||s (32 bytes + 32 bytes)

    public Es256SignatureProvider(ECDsa key, string kid, bool canSign)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentException.ThrowIfNullOrWhiteSpace(kid);

        _key = key;
        Kid = kid;
        _canSign = canSign;
    }

    public static Es256SignatureProvider GenerateKey(string kid)
    {
        var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return new Es256SignatureProvider(ecdsa, kid, canSign: true);
    }

    public static Es256SignatureProvider ImportPublic(ReadOnlySpan<byte> x, ReadOnlySpan<byte> y, string kid)
    {
        var parameters = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = x.ToArray(),
                Y = y.ToArray()
            }
        };

        var ecdsa = ECDsa.Create(parameters);
        return new Es256SignatureProvider(ecdsa, kid, canSign: false);
    }

    public static Es256SignatureProvider ImportPrivate(ReadOnlySpan<byte> x, ReadOnlySpan<byte> y, ReadOnlySpan<byte> d, string kid)
    {
        var parameters = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = x.ToArray(),
                Y = y.ToArray()
            },
            D = d.ToArray()
        };

        var ecdsa = ECDsa.Create(parameters);
        return new Es256SignatureProvider(ecdsa, kid, canSign: true);
    }

    public static Es256SignatureProvider ImportJwk(JsonWebKeyDto jwk)
    {
        ArgumentNullException.ThrowIfNull(jwk);
        if (!string.Equals(jwk.Kty, "EC", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(jwk.Crv, "P-256", StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("JWK must be an EC P-256 key.", nameof(jwk));
        }

        if (string.IsNullOrWhiteSpace(jwk.X) || string.IsNullOrWhiteSpace(jwk.Y))
        {
            throw new ArgumentException("JWK must contain x and y coordinates.", nameof(jwk));
        }

        byte[] x = Base64Url.DecodeFromChars(jwk.X);
        byte[] y = Base64Url.DecodeFromChars(jwk.Y);

        if (!string.IsNullOrWhiteSpace(jwk.D))
        {
            byte[] d = Base64Url.DecodeFromChars(jwk.D);
            return ImportPrivate(x, y, d, jwk.Kid);
        }

        return ImportPublic(x, y, jwk.Kid);
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

        bool success = _key.TrySignData(
            signingInput,
            destination[..SignatureSize],
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation,
            out int bytesWritten);

        if (!success || bytesWritten != SignatureSize)
        {
            throw new CryptographicException("Failed to generate ECDSA signature.");
        }
    }

    public bool Verify(ReadOnlySpan<byte> signingInput, ReadOnlySpan<byte> signature)
    {
        if (signature.Length != SignatureSize)
        {
            return false;
        }

        return _key.VerifyData(
            signingInput,
            signature,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    public JsonWebKeyDto ExportPublicJwk()
    {
        var parameters = _key.ExportParameters(includePrivateParameters: false);
        if (parameters.Q.X is null || parameters.Q.Y is null)
        {
            throw new CryptographicException("Failed to export EC public key parameters.");
        }

        return new JsonWebKeyDto
        {
            Kty = "EC",
            Crv = "P-256",
            Alg = Alg,
            Kid = Kid,
            Use = "sig",
            X = Base64Url.EncodeToString(parameters.Q.X),
            Y = Base64Url.EncodeToString(parameters.Q.Y)
        };
    }

    public JsonWebKeyDto ExportPrivateJwk()
    {
        if (!_canSign)
        {
            throw new InvalidOperationException("Key does not contain private material.");
        }

        var parameters = _key.ExportParameters(includePrivateParameters: true);
        if (parameters.Q.X is null || parameters.Q.Y is null || parameters.D is null)
        {
            throw new CryptographicException("Failed to export EC private key parameters.");
        }

        return new JsonWebKeyDto
        {
            Kty = "EC",
            Crv = "P-256",
            Alg = Alg,
            Kid = Kid,
            Use = "sig",
            X = Base64Url.EncodeToString(parameters.Q.X),
            Y = Base64Url.EncodeToString(parameters.Q.Y),
            D = Base64Url.EncodeToString(parameters.D)
        };
    }

    public byte[] ExportPrivateKeyBytes()
    {
        if (!_canSign)
        {
            throw new InvalidOperationException("Key does not contain private material.");
        }
        return _key.ExportPkcs8PrivateKey();
    }

    public static Es256SignatureProvider ImportPkcs8(ReadOnlySpan<byte> pkcs8Bytes, string kid)
    {
        var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(pkcs8Bytes, out _);
        return new Es256SignatureProvider(ecdsa, kid, canSign: true);
    }

    public void Dispose() => _key.Dispose();
}
