using System.Security.Cryptography;
using System.Text;

namespace Symbolon.Protocol;

/// <summary>
/// Standard hardware and platform component keys according to spec/08-fingerprint.md (FPR-1).
/// </summary>
public static class FingerprintComponentKeys
{
    public const string MachineId = "machineId";
    public const string Board = "board";
    public const string Cpu = "cpu";
    public const string Disk = "disk";
    public const string Mac = "mac";
    public const string Host = "host";

    public static readonly IReadOnlyList<string> StandardKeys =
    [
        MachineId,
        Board,
        Cpu,
        Disk,
        Mac,
        Host
    ];
}

/// <summary>
/// Helper for computing, sanitizing, and validating machine fingerprints and hashes according to LSE-4 and spec/08-fingerprint.md (FPR-1 to FPR-4).
/// </summary>
public static class FingerprintHelper
{
    private static readonly HashSet<string> PlaceholderValues = new(StringComparer.OrdinalIgnoreCase)
    {
        "unknown",
        "none",
        "0",
        "00000000-0000-0000-0000-000000000000",
        "null",
        "n/a",
        "default",
        "to be filled by o.e.m.",
        "not specified",
        "system serial number"
    };

    /// <summary>
    /// Filters out unavailable or invalid placeholder components according to FPR-3.
    /// Unavailable components MUST be omitted; placeholder strings (empty, "unknown", zeros) are strictly excluded.
    /// </summary>
    public static Dictionary<string, string> FilterValidComponents(IReadOnlyDictionary<string, string>? components)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (components is null) return result;

        foreach (var (key, value) in components)
        {
            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            string cleanKey = key.Trim();
            string cleanVal = value.Trim();

            if (PlaceholderValues.Contains(cleanVal))
            {
                // FPR-3: Omit placeholder values
                continue;
            }

            result[cleanKey] = cleanVal;
        }

        return result;
    }

    /// <summary>
    /// Pseudonymizes a hostname using a license-specific salt according to FPR-2.
    /// The raw hostname MUST NOT be transmitted or stored in plaintext.
    /// </summary>
    public static string PseudonymizeHost(string rawHost, string licenseSalt)
    {
        ArgumentNullException.ThrowIfNull(rawHost);
        ArgumentNullException.ThrowIfNull(licenseSalt);

        string cleanHost = rawHost.Trim().ToUpperInvariant();
        string cleanSalt = licenseSalt.Trim();

        byte[] input = Encoding.UTF8.GetBytes($"{cleanSalt}:{cleanHost}");
        byte[] hash = SHA256.HashData(input);
        return $"sha256:{Convert.ToHexStringLower(hash)}";
    }

    /// <summary>
    /// Computes canonical fingerprint representation from component dictionary (sorted alphabetically by key) according to FPR-4.
    /// </summary>
    public static string Canonicalize(IReadOnlyDictionary<string, string>? components)
    {
        var valid = FilterValidComponents(components);
        var sorted = valid.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase);

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
    /// Computes the sha256:{hex} summary hash of components according to FPR-4 and LSE-4.
    /// Components are sorted by code to produce a deterministic hash.
    /// </summary>
    public static string ComputeHash(IReadOnlyDictionary<string, string>? components)
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
