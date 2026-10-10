using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;

namespace Achilles.Crypto.SecretSharing;

/// <summary>
/// Reprezentácia jedného podielu (share) tajomstva v prahovej schéme Shamir's Secret Sharing.
/// </summary>
[SuppressMessage("Performance", "CA1819:Properties should not return arrays", Justification = "Kryptografické podiely pracujú s nemennými bajtovými poliami")]
public sealed record SecretShare(
    byte Index,
    byte Threshold,
    byte TotalShares,
    byte[] Data,
    byte[] Checksum)
{
    public const string TokenPrefix = "SYMBOLON-SHARE-v1";
    public const string PemHeader = "-----BEGIN SYMBOLON SECRET SHARE-----";
    public const string PemFooter = "-----END SYMBOLON SECRET SHARE-----";

    /// <summary>
    /// Vráti kompaktný reťazcový token reprezentujúci podiel (vhodný do CLI a konfiguračných súborov).
    /// Formát: SYMBOLON-SHARE-v1-{Threshold}-{TotalShares}-{Index}-{Base64Url(Data+Checksum)}
    /// </summary>
    public string ToToken()
    {
        byte[] payload = new byte[Data.Length + Checksum.Length];
        Buffer.BlockCopy(Data, 0, payload, 0, Data.Length);
        Buffer.BlockCopy(Checksum, 0, payload, Data.Length, Checksum.Length);

        string base64 = Convert.ToBase64String(payload)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

        return string.Create(CultureInfo.InvariantCulture, $"{TokenPrefix}-{Threshold}-{TotalShares}-{Index}-{base64}");
    }

    /// <summary>
    /// Vráti podiel formátovaný v štandardnom PEM bloku.
    /// </summary>
    public string ToPem()
    {
        var sb = new StringBuilder();
        sb.AppendLine(PemHeader);
        sb.AppendLine(CultureInfo.InvariantCulture, $"Version: 1");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Threshold: {Threshold}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"TotalShares: {TotalShares}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Index: {Index}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Checksum: {Convert.ToHexString(Checksum).ToUpperInvariant()}");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Data: {Convert.ToBase64String(Data)}");
        sb.AppendLine(PemFooter);
        return sb.ToString();
    }

    /// <summary>
    /// Pokúsi sa rozparsovať token alebo PEM formát podielu.
    /// </summary>
    public static bool TryParse(string input, out SecretShare? share)
    {
        share = null;
        if (string.IsNullOrWhiteSpace(input))
        {
            return false;
        }

        string trimmed = input.Trim();

        // 1. Skús formát tokenu
        if (trimmed.StartsWith(TokenPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string[] parts = trimmed.Split('-', 7);
            if (parts.Length == 7 &&
                string.Equals(parts[0], "SYMBOLON", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(parts[1], "SHARE", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(parts[2], "v1", StringComparison.OrdinalIgnoreCase))
            {
                if (byte.TryParse(parts[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte k) &&
                    byte.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte n) &&
                    byte.TryParse(parts[5], NumberStyles.Integer, CultureInfo.InvariantCulture, out byte x))
                {
                    try
                    {
                        string b64 = parts[6].Replace('-', '+').Replace('_', '/');
                        switch (b64.Length % 4)
                        {
                            case 2: b64 += "=="; break;
                            case 3: b64 += "="; break;
                        }
                        byte[] raw = Convert.FromBase64String(b64);
                        if (raw.Length >= 4)
                        {
                            int dataLen = raw.Length - 4;
                            byte[] data = new byte[dataLen];
                            byte[] checksum = new byte[4];
                            Buffer.BlockCopy(raw, 0, data, 0, dataLen);
                            Buffer.BlockCopy(raw, dataLen, checksum, 0, 4);

                            share = new SecretShare(x, k, n, data, checksum);
                            return true;
                        }
                    }
                    catch (FormatException)
                    {
                        return false;
                    }
                }
            }
        }

        // 2. Skús PEM formát
        if (trimmed.Contains(PemHeader, StringComparison.OrdinalIgnoreCase) &&
            trimmed.Contains(PemFooter, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                byte threshold = 0;
                byte total = 0;
                byte index = 0;
                byte[]? data = null;
                byte[]? checksum = null;

                using var reader = new StringReader(trimmed);
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    line = line.Trim();
                    if (line.StartsWith("Threshold:", StringComparison.OrdinalIgnoreCase))
                    {
                        threshold = byte.Parse(line.AsSpan("Threshold:".Length).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture);
                    }
                    else if (line.StartsWith("TotalShares:", StringComparison.OrdinalIgnoreCase))
                    {
                        total = byte.Parse(line.AsSpan("TotalShares:".Length).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture);
                    }
                    else if (line.StartsWith("Index:", StringComparison.OrdinalIgnoreCase))
                    {
                        index = byte.Parse(line.AsSpan("Index:".Length).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture);
                    }
                    else if (line.StartsWith("Checksum:", StringComparison.OrdinalIgnoreCase))
                    {
                        checksum = Convert.FromHexString(line.AsSpan("Checksum:".Length).Trim());
                    }
                    else if (line.StartsWith("Data:", StringComparison.OrdinalIgnoreCase))
                    {
                        data = Convert.FromBase64String(line.Substring("Data:".Length).Trim());
                    }
                }

                if (threshold >= 2 && total >= threshold && index >= 1 && data != null && checksum != null)
                {
                    share = new SecretShare(index, threshold, total, data, checksum);
                    return true;
                }
            }
            catch (Exception ex) when (ex is FormatException or OverflowException or InvalidOperationException)
            {
                return false;
            }
        }

        return false;
    }

    /// <summary>
    /// Rozparsuje token alebo vyhodí výnimku pri neplatnom formáte.
    /// </summary>
    public static SecretShare Parse(string input)
    {
        if (TryParse(input, out var share) && share != null)
        {
            return share;
        }

        throw new FormatException("Vstupný reťazec nie je platným formátom Symbolon Secret Share.");
    }
}
