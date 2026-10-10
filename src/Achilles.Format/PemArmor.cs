using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace Achilles.Format;

/// <summary>
/// PEM Armor encoding and decoding according to RFC 7468 and Symbolon LIC-1, LIC-2, LIC-4.
/// </summary>
public static class PemArmor
{
    private const int LineWidth = 64;

    /// <summary>
    /// Encapsulates binary data into PEM armor with the specified label and 64-character line wrapping.
    /// </summary>
    public static string Wrap(string label, ReadOnlySpan<byte> data)
    {
        string base64 = Convert.ToBase64String(data);
        var sb = new StringBuilder();

        sb.Append("-----BEGIN ").Append(label).Append("-----\n");

        for (int i = 0; i < base64.Length; i += LineWidth)
        {
            int length = Math.Min(LineWidth, base64.Length - i);
            sb.Append(base64, i, length).Append('\n');
        }

        sb.Append("-----END ").Append(label).Append("-----\n");

        return sb.ToString();
    }

    /// <summary>
    /// Attempts to extract binary data from a PEM-armored string.
    /// If the input is not PEM-armored, returns false.
    /// </summary>
    public static bool TryUnwrap(
        string pem,
        string expectedLabel,
        [NotNullWhen(true)] out byte[]? raw)
    {
        raw = null;
        if (string.IsNullOrWhiteSpace(pem))
        {
            return false;
        }

        string beginMarker = $"-----BEGIN {expectedLabel}-----";
        string endMarker = $"-----END {expectedLabel}-----";

        int beginIndex = pem.IndexOf(beginMarker, StringComparison.Ordinal);
        if (beginIndex < 0)
        {
            return false;
        }

        int contentStart = beginIndex + beginMarker.Length;
        int endIndex = pem.IndexOf(endMarker, contentStart, StringComparison.Ordinal);
        if (endIndex < 0)
        {
            return false;
        }

        string base64Content = pem.Substring(contentStart, endIndex - contentStart);
        // Remove whitespace and newlines
        string cleanBase64 = string.Concat(base64Content.Where(c => !char.IsWhiteSpace(c)));

        try
        {
            raw = Convert.FromBase64String(cleanBase64);
            return true;
        }
        catch (FormatException)
        {
            raw = null;
            return false;
        }
    }
}
