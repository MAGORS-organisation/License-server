using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Symbolon.Crypto;

public sealed record EphemeralPossessionKeyPair(
    string PublicKeyJwk,
    string PrivateKeyJwk);

/// <summary>
/// Cryptographic engine for generating possession key pairs and verifying proof-of-possession
/// challenges for early return of offline roaming borrowed seats (FLT-21, FLT-22).
/// </summary>
public static class ProofOfPossessionEngine
{
    private sealed class JwkEcKey
    {
        [System.Text.Json.Serialization.JsonPropertyName("kty")]
        public string? Kty { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("crv")]
        public string? Crv { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("x")]
        public string? X { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("y")]
        public string? Y { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("d")]
        [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
        public string? D { get; set; }
    }

    /// <summary>
    /// Generates an ephemeral NIST P-256 ECDSA key pair for offline possession proof.
    /// </summary>
    public static EphemeralPossessionKeyPair GenerateKeyPair()
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var ecParams = ecdsa.ExportParameters(includePrivateParameters: true);

        string x = Base64Url.EncodeToString(ecParams.Q.X!);
        string y = Base64Url.EncodeToString(ecParams.Q.Y!);
        string d = Base64Url.EncodeToString(ecParams.D!);

        var pubJwk = new JwkEcKey
        {
            Kty = "EC",
            Crv = "P-256",
            X = x,
            Y = y
        };

        var privJwk = new JwkEcKey
        {
            Kty = "EC",
            Crv = "P-256",
            X = x,
            Y = y,
            D = d
        };

        return new EphemeralPossessionKeyPair(
            PublicKeyJwk: JsonSerializer.Serialize(pubJwk),
            PrivateKeyJwk: JsonSerializer.Serialize(privJwk));
    }

    /// <summary>
    /// Signs a challenge nonce with the possession private key.
    /// </summary>
    public static string SignChallenge(string nonce, string privateKeyJwk)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nonce);
        ArgumentException.ThrowIfNullOrWhiteSpace(privateKeyJwk);

        var key = JsonSerializer.Deserialize<JwkEcKey>(privateKeyJwk)
            ?? throw new ArgumentException("Invalid private key JWK JSON.", nameof(privateKeyJwk));

        if (key.Kty != "EC" || key.Crv != "P-256" || string.IsNullOrWhiteSpace(key.X) || string.IsNullOrWhiteSpace(key.Y) || string.IsNullOrWhiteSpace(key.D))
        {
            throw new ArgumentException("JWK must be a valid EC P-256 private key with d, x, y parameters.", nameof(privateKeyJwk));
        }

        var ecParams = new ECParameters
        {
            Curve = ECCurve.NamedCurves.nistP256,
            Q = new ECPoint
            {
                X = Base64Url.DecodeFromChars(key.X),
                Y = Base64Url.DecodeFromChars(key.Y)
            },
            D = Base64Url.DecodeFromChars(key.D)
        };

        using var ecdsa = ECDsa.Create(ecParams);
        byte[] nonceBytes = Encoding.UTF8.GetBytes(nonce);
        byte[] signatureBytes = ecdsa.SignData(nonceBytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return Base64Url.EncodeToString(signatureBytes);
    }

    /// <summary>
    /// Verifies the cryptographic proof-of-possession signature against the stored possession public key.
    /// </summary>
    public static bool VerifyProof(string nonce, string signature, string publicKeyJwk)
    {
        if (string.IsNullOrWhiteSpace(nonce) || string.IsNullOrWhiteSpace(signature) || string.IsNullOrWhiteSpace(publicKeyJwk))
        {
            return false;
        }

        JwkEcKey? key;
        try
        {
            key = JsonSerializer.Deserialize<JwkEcKey>(publicKeyJwk);
        }
        catch (JsonException)
        {
            return false;
        }

        if (key is null || key.Kty != "EC" || key.Crv != "P-256" || string.IsNullOrWhiteSpace(key.X) || string.IsNullOrWhiteSpace(key.Y))
        {
            return false;
        }

        byte[] sigBytes;
        try
        {
            sigBytes = Base64Url.DecodeFromChars(signature);
        }
        catch (FormatException)
        {
            return false;
        }

        try
        {
            var ecParams = new ECParameters
            {
                Curve = ECCurve.NamedCurves.nistP256,
                Q = new ECPoint
                {
                    X = Base64Url.DecodeFromChars(key.X),
                    Y = Base64Url.DecodeFromChars(key.Y)
                }
            };

            using var ecdsa = ECDsa.Create(ecParams);
            byte[] nonceBytes = Encoding.UTF8.GetBytes(nonce);
            return ecdsa.VerifyData(nonceBytes, sigBytes, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }
}
