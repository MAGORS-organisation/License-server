namespace Achilles.Crypto;

/// <summary>
/// Provides cryptographic signing and signature verification operations.
/// </summary>
public interface ISignatureProvider : IDisposable
{
    /// <summary>JOSE "alg" value (e.g. ES256, ML-DSA-65).</summary>
    string Alg { get; }

    /// <summary>Key ID (kid).</summary>
    string Kid { get; }

    /// <summary>Whether this provider holds a private key and can sign data.</summary>
    bool CanSign { get; }

    /// <summary>Exact size of signatures produced by this provider in bytes.</summary>
    int SignatureSize { get; }

    /// <summary>Signs the given input bytes and writes the raw signature into destination.</summary>
    void Sign(ReadOnlySpan<byte> signingInput, Span<byte> destination);

    /// <summary>Verifies whether the given signature is valid for the input bytes.</summary>
    bool Verify(ReadOnlySpan<byte> signingInput, ReadOnlySpan<byte> signature);

    /// <summary>Exports the public key in JWK format.</summary>
    JsonWebKeyDto ExportPublicJwk();
}
