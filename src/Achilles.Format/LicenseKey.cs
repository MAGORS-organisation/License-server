using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;

namespace Achilles.Format;

/// <summary>
/// Symbolon License Key conforming to spec/02-license-key.md (KEY-1 to KEY-14).
/// Format: {PREFIX}-{G1}-{G2}-{G3}-{G4}-{G5}
/// G1-G4: 20 characters = 100 bits entropy
/// G5: 5 characters = CRC-32C top 25 bits
/// </summary>
public sealed class LicenseKey
{
    public const string DefaultPrefix = "SYM";

    public string Prefix { get; }
    public string Entropy { get; } // 20 characters (G1..G4)
    public string Checksum { get; } // 5 characters (G5)
    public string Canonical { get; } // Normalized canonical formatted string

    private LicenseKey(string prefix, string entropy, string checksum, string canonical)
    {
        Prefix = prefix;
        Entropy = entropy;
        Checksum = checksum;
        Canonical = canonical;
    }

    /// <summary>
    /// Generates a new cryptographically secure License Key with the given prefix.
    /// </summary>
    public static LicenseKey Generate(string prefix = DefaultPrefix)
    {
        string normPrefix = CrockfordBase32.Normalize(prefix);
        ValidatePrefix(normPrefix);

        // Generate 100 bits of CSPRNG entropy as 20 Crockford Base32 characters (each 5 bits)
        var entropyChars = new char[20];
        for (int i = 0; i < 20; i++)
        {
            int index = RandomNumberGenerator.GetInt32(32);
            entropyChars[i] = CrockfordBase32.Alphabet[index];
        }

        string entropy = new(entropyChars);
        string checksum = ComputeChecksum(entropy);
        string canonical = FormatKey(normPrefix, entropy, checksum);

        return new LicenseKey(normPrefix, entropy, checksum, canonical);
    }

    /// <summary>
    /// Attempts to parse and validate a License Key according to KEY-6, KEY-7, KEY-8.
    /// </summary>
    public static bool TryParse(
        string? input,
        [NotNullWhen(true)] out LicenseKey? licenseKey,
        [NotNullWhen(false)] out string? error)
    {
        licenseKey = null;
        error = null;

        if (string.IsNullOrWhiteSpace(input))
        {
            error = "empty-license-key";
            return false;
        }

        // KEY-6: 1. strip whitespace and dashes, 2. uppercase, 3. I/L->1, O->0
        string normalized = CrockfordBase32.Normalize(input);

        // A key must have prefix (2-8 chars) + 25 chars (20 data + 5 checksum) = 27 to 33 characters
        if (normalized.Length < 27 || normalized.Length > 33)
        {
            error = "invalid-key-length";
            return false;
        }

        // Validate all characters are valid Crockford Base32
        foreach (char c in normalized)
        {
            if (!CrockfordBase32.IsValidChar(c))
            {
                error = $"invalid-character:{c}";
                return false;
            }
        }

        int prefixLength = normalized.Length - 25;
        string prefix = normalized[..prefixLength];
        if (prefix.Length < 2 || prefix.Length > 8)
        {
            error = "invalid-prefix-length";
            return false;
        }

        string entropy = normalized.Substring(prefixLength, 20);
        string checksum = normalized.Substring(prefixLength + 20, 5);

        // KEY-8: Verify CRC-32C checksum
        string expectedChecksum = ComputeChecksum(entropy);
        if (!string.Equals(checksum, expectedChecksum, StringComparison.Ordinal))
        {
            error = "checksum-mismatch"; // preklep v kľúči (KEY-8)
            return false;
        }

        string canonical = FormatKey(prefix, entropy, checksum);
        licenseKey = new LicenseKey(prefix, entropy, checksum, canonical);
        return true;
    }

    public static LicenseKey Parse(string input)
    {
        if (!TryParse(input, out var key, out string? error))
        {
            throw new FormatException($"Invalid license key: {error}");
        }
        return key;
    }

    public override string ToString() => Canonical;

    private static void ValidatePrefix(string prefix)
    {
        if (prefix.Length < 2 || prefix.Length > 8)
        {
            throw new ArgumentException("Prefix must contain 2 to 8 characters (KEY-5).", nameof(prefix));
        }

        foreach (char c in prefix)
        {
            if (!CrockfordBase32.IsValidChar(c))
            {
                throw new ArgumentException($"Invalid character in prefix: '{c}' (KEY-5).", nameof(prefix));
            }
        }
    }

    /// <summary>
    /// Computes CRC-32C over the 20 normalized entropy characters, truncated to 25 MSBs, encoded in 5 Crockford chars.
    /// </summary>
    public static string ComputeChecksum(string normalizedEntropy20)
    {
        byte[] asciiBytes = Encoding.ASCII.GetBytes(normalizedEntropy20);
        uint crc32 = Crc32C.Compute(asciiBytes);

        // KEY-3: 25 most significant bits of CRC-32C
        uint crc25 = (crc32 >> 7) & 0x01FFFFFF;

        Span<char> chars = stackalloc char[5];
        chars[0] = CrockfordBase32.Alphabet[(int)((crc25 >> 20) & 0x1F)];
        chars[1] = CrockfordBase32.Alphabet[(int)((crc25 >> 15) & 0x1F)];
        chars[2] = CrockfordBase32.Alphabet[(int)((crc25 >> 10) & 0x1F)];
        chars[3] = CrockfordBase32.Alphabet[(int)((crc25 >> 5) & 0x1F)];
        chars[4] = CrockfordBase32.Alphabet[(int)(crc25 & 0x1F)];

        return new string(chars);
    }

    private static string FormatKey(string prefix, string entropy, string checksum)
    {
        // {PREFIX}-{G1}-{G2}-{G3}-{G4}-{G5}
        return $"{prefix}-{entropy[..5]}-{entropy.Substring(5, 5)}-{entropy.Substring(10, 5)}-{entropy.Substring(15, 5)}-{checksum}";
    }
}
