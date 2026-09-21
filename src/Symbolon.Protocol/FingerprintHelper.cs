using System.Security.Cryptography;
using System.Text;

namespace Symbolon.Protocol;

/// <summary>
/// Helper for computing and verifying machine fingerprints and hashes according to LSE-4 and spec/08-fingerprint.md.
/// </summary>
public static class FingerprintHelper
{
    /// <summary>
    /// Computes canonical fingerprint representation from component dictionary (sorted alphabetically by key).
    /// </summary>
    public static string Canonicalize(IReadOnlyDictionary<string, string> components)
    {
        ArgumentNullException.ThrowIfNull(components);

        var sorted = components.OrderBy(kv => kv.Key, StringComparer.Ordinal);
        var sb = new StringBuilder();
        foreach (var (key, val) in sorted)
        {
            sb.Append(key.Trim().ToUpperInvariant())
              .Append('=')
              .Append(val.Trim())
              .Append('\n');
        }
        return sb.ToString();
    }

    /// <summary>
    /// Computes the sha256:{hex} hash of components according to LSE-4.
    /// </summary>
    public static string ComputeHash(IReadOnlyDictionary<string, string> components)
    {
        string canonical = Canonicalize(components);
        byte[] bytes = Encoding.UTF8.GetBytes(canonical);
        byte[] hash = SHA256.HashData(bytes);
        return $"sha256:{Convert.ToHexStringLower(hash)}";
    }

    /// <summary>
    /// Computes the sha256:{hex} hash directly from raw string data.
    /// </summary>
    public static string ComputeHash(string rawData)
    {
        ArgumentNullException.ThrowIfNull(rawData);
        byte[] bytes = Encoding.UTF8.GetBytes(rawData);
        byte[] hash = SHA256.HashData(bytes);
        return $"sha256:{Convert.ToHexStringLower(hash)}";
    }
}
