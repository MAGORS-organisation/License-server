using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Achilles.Crypto;

/// <summary>
/// Thread-safe in-memory key ring implementation holding trusted signature providers and revocations.
/// </summary>
public sealed class AchillesKeyRing : IKeyRing, IDisposable
{
    private readonly ConcurrentDictionary<(string Kid, string Alg), ISignatureProvider> _keys = new();
    private readonly ConcurrentDictionary<string, byte> _revokedKids = new(StringComparer.Ordinal);

    public void Add(ISignatureProvider key)
    {
        ArgumentNullException.ThrowIfNull(key);
        _keys[(key.Kid, key.Alg)] = key;
    }

    public void Revoke(string kid)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kid);
        _revokedKids[kid] = 1;
    }

    public bool TryGet(string kid, string alg, [NotNullWhen(true)] out ISignatureProvider? key)
    {
        return _keys.TryGetValue((kid, alg), out key);
    }

    public bool IsRevoked(string kid)
    {
        return _revokedKids.ContainsKey(kid);
    }

    public void Dispose()
    {
        foreach (var key in _keys.Values)
        {
            key.Dispose();
        }
        _keys.Clear();
    }
}
