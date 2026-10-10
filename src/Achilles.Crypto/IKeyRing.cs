using System.Diagnostics.CodeAnalysis;

namespace Achilles.Crypto;

/// <summary>
/// Interface for looking up trusted verification keys and revocation status.
/// </summary>
public interface IKeyRing
{
    /// <summary>
    /// Attempts to retrieve a signature provider for the specified key ID (kid) and algorithm (alg).
    /// </summary>
    bool TryGet(string kid, string alg, [NotNullWhen(true)] out ISignatureProvider? key);

    /// <summary>
    /// Checks whether the specified key ID has been explicitly revoked.
    /// </summary>
    bool IsRevoked(string kid);
}
