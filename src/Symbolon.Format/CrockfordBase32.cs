using System.Text;

namespace Symbolon.Format;

/// <summary>
/// Crockford Base32 encoding, decoding, and normalization according to KEY-4 and KEY-6.
/// Alphabet: 0123456789ABCDEFGHJKMNPQRSTVWXYZ
/// </summary>
public static class CrockfordBase32
{
    public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";

    private static readonly sbyte[] DecodeTable = InitializeDecodeTable();

    private static sbyte[] InitializeDecodeTable()
    {
        var table = new sbyte[128];
        Array.Fill(table, (sbyte)-1);

        for (int i = 0; i < Alphabet.Length; i++)
        {
            char c = Alphabet[i];
            table[c] = (sbyte)i;
            if (char.IsAsciiLetterUpper(c))
            {
                table[char.ToLowerInvariant(c)] = (sbyte)i;
            }
        }

        // Permissive mappings according to Crockford:
        // O / o -> 0
        table['O'] = 0;
        table['o'] = 0;
        // I / i / L / l -> 1
        table['I'] = 1;
        table['i'] = 1;
        table['L'] = 1;
        table['l'] = 1;

        return table;
    }

    /// <summary>
    /// Checks whether the character is valid in Crockford Base32.
    /// </summary>
    public static bool IsValidChar(char c)
    {
        return c < 128 && DecodeTable[c] >= 0;
    }

    /// <summary>
    /// Decodes a single character to its 5-bit value (0-31). Returns -1 if invalid.
    /// </summary>
    public static int DecodeChar(char c)
    {
        if (c >= 128) return -1;
        return DecodeTable[c];
    }

    /// <summary>
    /// Normalizes input string according to KEY-6:
    /// 1. Remove whitespace and hyphens
    /// 2. Convert to uppercase
    /// 3. Replace I -> 1, L -> 1, O -> 0
    /// </summary>
    public static string Normalize(string input)
    {
        if (string.IsNullOrEmpty(input)) return string.Empty;

        var sb = new StringBuilder(input.Length);
        foreach (char ch in input)
        {
            if (char.IsWhiteSpace(ch) || ch == '-')
            {
                continue;
            }

            char upper = char.ToUpperInvariant(ch);
            char normalized = upper switch
            {
                'I' or 'L' => '1',
                'O' => '0',
                _ => upper
            };

            sb.Append(normalized);
        }

        return sb.ToString();
    }
}
