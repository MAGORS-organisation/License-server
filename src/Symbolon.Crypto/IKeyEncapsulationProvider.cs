namespace Symbolon.Crypto;

/// <summary>
/// Abstraction for Post-Quantum Key Encapsulation Mechanisms (FIPS 203 ML-KEM).
/// Provides encapsulation of shared secrets and decapsulation for quantum-safe key exchange.
/// </summary>
public interface IKeyEncapsulationProvider : IDisposable
{
    string Alg { get; }
    string Kid { get; }
    bool CanDecapsulate { get; }
    int CiphertextSize { get; }
    int SharedSecretSize { get; }

    /// <summary>
    /// Encapsulates a new ephemeral shared secret using the recipient's public encapsulation key.
    /// </summary>
    (byte[] Ciphertext, byte[] SharedSecret) Encapsulate();

    /// <summary>
    /// Encapsulates a new ephemeral shared secret into the provided destination spans.
    /// </summary>
    void Encapsulate(Span<byte> ciphertext, Span<byte> sharedSecret);

    /// <summary>
    /// Decapsulates the shared secret from the provided ciphertext using the private decapsulation key.
    /// </summary>
    byte[] Decapsulate(ReadOnlySpan<byte> ciphertext);

    /// <summary>
    /// Decapsulates the shared secret into the provided destination span.
    /// </summary>
    void Decapsulate(ReadOnlySpan<byte> ciphertext, Span<byte> destination);

    /// <summary>
    /// Exports the key material as a JSON Web Key (JWK) conforming to RFC 9964 / AKP.
    /// </summary>
    JsonWebKeyDto ExportJwk(bool includePrivate = false);
}
