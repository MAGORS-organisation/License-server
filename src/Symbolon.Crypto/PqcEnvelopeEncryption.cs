using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;

namespace Symbolon.Crypto;

/// <summary>
/// Encrypted envelope structure for Post-Quantum payload protection.
/// Combines FIPS 203 ML-KEM key encapsulation with AES-256-GCM symmetric encryption.
/// </summary>
public sealed record PqcEncryptedEnvelope
{
    [JsonPropertyName("alg")]
    public required string Alg { get; init; }

    [JsonPropertyName("kid")]
    public required string Kid { get; init; }

    [JsonPropertyName("ciphertext")]
    public required string Ciphertext { get; init; }

    [JsonPropertyName("nonce")]
    public required string Nonce { get; init; }

    [JsonPropertyName("tag")]
    public required string Tag { get; init; }

    [JsonPropertyName("payload")]
    public required string Payload { get; init; }
}

/// <summary>
/// Post-Quantum envelope encryption engine for quantum-safe data exchange and offline token storage.
/// Protects against Harvest-Now-Decrypt-Later (HNDL) attacks.
/// </summary>
public static class PqcEnvelopeEncryption
{
    private const int NonceSizeBytes = 12; // Standard 96-bit GCM nonce
    private const int TagSizeBytes = 16;   // 128-bit GCM auth tag

    /// <summary>
    /// Encrypts plaintext data using recipient's ML-KEM encapsulation key and AES-256-GCM.
    /// </summary>
    public static PqcEncryptedEnvelope Encrypt(byte[] plaintext, IKeyEncapsulationProvider recipientKey)
    {
        ArgumentNullException.ThrowIfNull(plaintext);
        ArgumentNullException.ThrowIfNull(recipientKey);

        // 1. ML-KEM encapsulation produces ephemeral ciphertext and 32-byte shared secret
        var (ctBytes, sharedSecret) = recipientKey.Encapsulate();

        try
        {
            // 2. Generate random 12-byte nonce
            byte[] nonce = new byte[NonceSizeBytes];
            RandomNumberGenerator.Fill(nonce);

            // 3. Encrypt payload with AES-256-GCM
            byte[] tag = new byte[TagSizeBytes];
            byte[] ciphertext = new byte[plaintext.Length];

            using (var aesGcm = new AesGcm(sharedSecret, TagSizeBytes))
            {
                // Authenticated Associated Data binds algorithm and recipient key ID
                byte[] aad = Encoding.UTF8.GetBytes($"{recipientKey.Alg}:{recipientKey.Kid}");
                aesGcm.Encrypt(nonce, plaintext, ciphertext, tag, aad);
            }

            return new PqcEncryptedEnvelope
            {
                Alg = $"{recipientKey.Alg}+A256GCM",
                Kid = recipientKey.Kid,
                Ciphertext = Base64Url.EncodeToString(ctBytes),
                Nonce = Base64Url.EncodeToString(nonce),
                Tag = Base64Url.EncodeToString(tag),
                Payload = Base64Url.EncodeToString(ciphertext)
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sharedSecret);
        }
    }

    /// <summary>
    /// Decrypts a post-quantum encrypted envelope using the recipient's ML-KEM decapsulation key.
    /// </summary>
    public static byte[] Decrypt(PqcEncryptedEnvelope envelope, IKeyEncapsulationProvider recipientKey)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        ArgumentNullException.ThrowIfNull(recipientKey);

        if (!recipientKey.CanDecapsulate)
        {
            throw new InvalidOperationException("Recipient key does not contain private decapsulation material.");
        }

        byte[] ctBytes = Base64Url.DecodeFromChars(envelope.Ciphertext);
        byte[] nonce = Base64Url.DecodeFromChars(envelope.Nonce);
        byte[] tag = Base64Url.DecodeFromChars(envelope.Tag);
        byte[] ciphertext = Base64Url.DecodeFromChars(envelope.Payload);

        // 1. Decapsulate shared secret using private ML-KEM key
        byte[] sharedSecret = recipientKey.Decapsulate(ctBytes);

        try
        {
            // 2. Decrypt payload with AES-256-GCM
            byte[] plaintext = new byte[ciphertext.Length];
            using (var aesGcm = new AesGcm(sharedSecret, TagSizeBytes))
            {
                byte[] aad = Encoding.UTF8.GetBytes($"{recipientKey.Alg}:{envelope.Kid}");
                aesGcm.Decrypt(nonce, ciphertext, tag, plaintext, aad);
            }

            return plaintext;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sharedSecret);
        }
    }
}
