using System.Text;

namespace Achilles.Format;

/// <summary>
/// Error correction levels for QR Code generation (ISO/IEC 18004).
/// </summary>
public enum QrErrorCorrectionLevel
{
    /// <summary>Level L (7% redundancy)</summary>
    L = 1,
    /// <summary>Level M (15% redundancy)</summary>
    M = 0,
    /// <summary>Level Q (25% redundancy)</summary>
    Q = 3,
    /// <summary>Level H (30% redundancy)</summary>
    H = 2
}

/// <summary>
/// Lightweight, dependency-free QR Code model 2 matrix generator and ASCII/ANSI renderer.
/// Supports Byte mode encoding for ISO/IEC 18004 QR Codes up to Version 6 (41x41).
/// </summary>
public static class QrCodeEncoder
{
    private static readonly byte[] GfExp = new byte[512];
    private static readonly byte[] GfLog = new byte[256];

    static QrCodeEncoder()
    {
        int val = 1;
        for (int i = 0; i < 255; i++)
        {
            GfExp[i] = (byte)val;
            GfExp[i + 255] = (byte)val;
            GfLog[val] = (byte)i;
            val <<= 1;
            if ((val & 0x100) != 0)
            {
                val ^= 0x11D; // x^8 + x^4 + x^3 + x^2 + 1
            }
        }
    }

    private static byte GfMul(byte a, byte b) =>
        (a == 0 || b == 0) ? (byte)0 : GfExp[GfLog[a] + GfLog[b]];

    private sealed record VersionParams(int Version, int TotalCodewords, int DataCodewords, int EcCodewords, int Blocks);

    private static readonly VersionParams[] LevelLTable =
    [
        new(1, 26, 19, 7, 1),
        new(2, 44, 34, 10, 1),
        new(3, 70, 55, 15, 1),
        new(4, 100, 80, 20, 1),
        new(5, 134, 108, 26, 1),
        new(6, 172, 136, 18, 2)
    ];

    private static readonly VersionParams[] LevelMTable =
    [
        new(1, 26, 16, 10, 1),
        new(2, 44, 28, 16, 1),
        new(3, 70, 44, 26, 1),
        new(4, 100, 64, 18, 2),
        new(5, 134, 86, 24, 2),
        new(6, 172, 108, 16, 4)
    ];

    private static bool[][] CreateMatrix(int size)
    {
        var m = new bool[size][];
        for (int i = 0; i < size; i++)
        {
            m[i] = new bool[size];
        }
        return m;
    }

    /// <summary>
    /// Encodes arbitrary text into a 2D boolean jagged array (true = dark, false = light).
    /// </summary>
    public static bool[][] Encode(string text, QrErrorCorrectionLevel level = QrErrorCorrectionLevel.M)
    {
        ArgumentNullException.ThrowIfNull(text);
        byte[] raw = Encoding.UTF8.GetBytes(text);

        var table = level == QrErrorCorrectionLevel.L ? LevelLTable : LevelMTable;
        VersionParams? match = null;
        foreach (var p in table)
        {
            // Byte mode overhead: 4 bits mode + 8 bits count = 12 bits = 2 bytes
            if (raw.Length + 2 <= p.DataCodewords)
            {
                match = p;
                break;
            }
        }

        if (match is null)
        {
            throw new ArgumentException($"Payload too long ({raw.Length} bytes) for compact QR encoder (max {table[^1].DataCodewords - 2} bytes).", nameof(text));
        }

        // 1. Bitstream assembly
        var bits = new List<bool>();
        AddBits(bits, 0b0100, 4);
        AddBits(bits, raw.Length, 8);
        foreach (byte b in raw)
        {
            AddBits(bits, b, 8);
        }

        int maxDataBits = match.DataCodewords * 8;
        int terminatorLen = Math.Min(4, maxDataBits - bits.Count);
        for (int i = 0; i < terminatorLen; i++) bits.Add(false);

        while (bits.Count % 8 != 0) bits.Add(false);

        byte[] padBytes = [0xEC, 0x11];
        int padIdx = 0;
        while (bits.Count < maxDataBits)
        {
            AddBits(bits, padBytes[padIdx % 2], 8);
            padIdx++;
        }

        byte[] dataCodewords = new byte[match.DataCodewords];
        for (int i = 0; i < match.DataCodewords; i++)
        {
            int val = 0;
            for (int b = 0; b < 8; b++)
            {
                if (bits[i * 8 + b]) val |= (1 << (7 - b));
            }
            dataCodewords[i] = (byte)val;
        }

        // 2. Reed-Solomon Error Correction
        int ecCountPerBlock = match.EcCodewords;
        byte[] genPoly = BuildGeneratorPoly(ecCountPerBlock);

        int dataPerBlock = match.DataCodewords / match.Blocks;
        byte[][] dataBlocks = new byte[match.Blocks][];
        byte[][] ecBlocks = new byte[match.Blocks][];

        for (int b = 0; b < match.Blocks; b++)
        {
            dataBlocks[b] = new byte[dataPerBlock];
            Array.Copy(dataCodewords, b * dataPerBlock, dataBlocks[b], 0, dataPerBlock);
            ecBlocks[b] = ComputeEcRemainder(dataBlocks[b], genPoly, ecCountPerBlock);
        }

        var finalCodewords = new List<byte>(match.TotalCodewords);
        for (int i = 0; i < dataPerBlock; i++)
        {
            for (int b = 0; b < match.Blocks; b++)
            {
                finalCodewords.Add(dataBlocks[b][i]);
            }
        }
        for (int i = 0; i < ecCountPerBlock; i++)
        {
            for (int b = 0; b < match.Blocks; b++)
            {
                finalCodewords.Add(ecBlocks[b][i]);
            }
        }

        // 3. Matrix layout
        int size = 17 + 4 * match.Version;
        bool[][] matrix = CreateMatrix(size);
        bool[][] isFunction = CreateMatrix(size);

        PlaceFinderPatterns(matrix, isFunction, size);
        PlaceTimingPatterns(matrix, isFunction, size);
        if (match.Version >= 2)
        {
            PlaceAlignmentPattern(matrix, isFunction, size);
        }
        ReserveFormatInfo(isFunction, size);

        PlaceDataBits(matrix, isFunction, size, finalCodewords);

        int minPenalty = int.MaxValue;
        bool[][] bestMatrix = matrix;

        for (int mask = 0; mask < 8; mask++)
        {
            bool[][] masked = ApplyMask(matrix, isFunction, size, mask);
            EmbedFormatInfo(masked, size, level, mask);
            int penalty = CalculatePenalty(masked, size);
            if (penalty < minPenalty)
            {
                minPenalty = penalty;
                bestMatrix = masked;
            }
        }

        return bestMatrix;
    }

    /// <summary>
    /// Renders a QR code matrix into high-contrast Unicode half-block characters for terminal output.
    /// Each terminal character cell renders two vertical modules, ensuring a clean square aspect ratio.
    /// </summary>
    public static string RenderAscii(string text, QrErrorCorrectionLevel level = QrErrorCorrectionLevel.M, int quietZone = 2)
    {
        bool[][] matrix = Encode(text, level);
        int size = matrix.Length;
        int totalSize = size + quietZone * 2;

        bool[][] canvas = CreateMatrix(totalSize);
        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                canvas[r + quietZone][c + quietZone] = matrix[r][c];
            }
        }

        var sb = new StringBuilder();
        for (int r = 0; r < totalSize; r += 2)
        {
            for (int c = 0; c < totalSize; c++)
            {
                bool top = canvas[r][c];
                bool bottom = r + 1 < totalSize && canvas[r + 1][c];

                if (top && bottom)
                {
                    sb.Append("██");
                }
                else if (top && !bottom)
                {
                    sb.Append("▀▀");
                }
                else if (!top && bottom)
                {
                    sb.Append("▄▄");
                }
                else
                {
                    sb.Append("  ");
                }
            }
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// Renders the QR code inside a decorative Retro DOS / FoxPro double-bordered dialog box.
    /// </summary>
    public static string RenderRetroBox(string text, string title = "AIR-GAP QR CODE", QrErrorCorrectionLevel level = QrErrorCorrectionLevel.M)
    {
        ArgumentNullException.ThrowIfNull(title);
        string asciiQr = RenderAscii(text, level, quietZone: 1);
        string[] lines = asciiQr.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);

        int contentWidth = lines.Length > 0 ? lines[0].Length : 20;
        int boxWidth = Math.Max(contentWidth + 4, title.Length + 8);

        var sb = new StringBuilder();
        string cleanTitle = $" {title.Trim()} ";
        int leftPad = (boxWidth - cleanTitle.Length - 2) / 2;
        int rightPad = boxWidth - cleanTitle.Length - 2 - leftPad;

        sb.Append('╔').Append(new string('═', leftPad)).Append(cleanTitle).Append(new string('═', rightPad)).AppendLine("╗");
        sb.Append('║').Append(new string(' ', boxWidth - 2)).AppendLine("║");

        foreach (var line in lines)
        {
            int linePad = (boxWidth - 2 - line.Length) / 2;
            int rLinePad = boxWidth - 2 - line.Length - linePad;
            sb.Append('║').Append(new string(' ', linePad)).Append(line).Append(new string(' ', rLinePad)).AppendLine("║");
        }

        sb.Append('║').Append(new string(' ', boxWidth - 2)).AppendLine("║");
        sb.Append('╚').Append(new string('═', boxWidth - 2)).AppendLine("╝");

        return sb.ToString();
    }

    private static void AddBits(List<bool> bits, int value, int count)
    {
        for (int i = count - 1; i >= 0; i--)
        {
            bits.Add(((value >> i) & 1) == 1);
        }
    }

    private static byte[] BuildGeneratorPoly(int ecCount)
    {
        var poly = new byte[ecCount + 1];
        poly[0] = 1;
        for (int i = 0; i < ecCount; i++)
        {
            byte factor = GfExp[i];
            for (int j = i + 1; j >= 1; j--)
            {
                poly[j] = (byte)(poly[j] ^ GfMul(poly[j - 1], factor));
            }
            poly[0] = GfMul(poly[0], factor);
        }
        Array.Reverse(poly);
        return poly;
    }

    private static byte[] ComputeEcRemainder(byte[] data, byte[] genPoly, int ecCount)
    {
        byte[] rem = new byte[ecCount];
        foreach (byte b in data)
        {
            byte lead = (byte)(b ^ rem[0]);
            for (int j = 0; j < ecCount - 1; j++)
            {
                rem[j] = (byte)(rem[j + 1] ^ GfMul(lead, genPoly[j + 1]));
            }
            rem[ecCount - 1] = GfMul(lead, genPoly[ecCount]);
        }
        return rem;
    }

    private static void PlaceFinderPatterns(bool[][] matrix, bool[][] isFunc, int size)
    {
        PlaceOneFinder(matrix, isFunc, 0, 0);
        PlaceOneFinder(matrix, isFunc, 0, size - 7);
        PlaceOneFinder(matrix, isFunc, size - 7, 0);

        for (int i = 0; i < 8; i++)
        {
            SetFunc(matrix, isFunc, 7, i, false);
            SetFunc(matrix, isFunc, i, 7, false);

            SetFunc(matrix, isFunc, 7, size - 8 + i, false);
            SetFunc(matrix, isFunc, i, size - 8, false);

            SetFunc(matrix, isFunc, size - 8, i, false);
            SetFunc(matrix, isFunc, size - 8 + i, 7, false);
        }
    }

    private static void PlaceOneFinder(bool[][] matrix, bool[][] isFunc, int startRow, int startCol)
    {
        for (int r = 0; r < 7; r++)
        {
            for (int c = 0; c < 7; c++)
            {
                bool dark = (r == 0 || r == 6 || c == 0 || c == 6) || (r >= 2 && r <= 4 && c >= 2 && c <= 4);
                SetFunc(matrix, isFunc, startRow + r, startCol + c, dark);
            }
        }
    }

    private static void PlaceTimingPatterns(bool[][] matrix, bool[][] isFunc, int size)
    {
        for (int i = 8; i < size - 8; i++)
        {
            bool dark = (i % 2 == 0);
            if (!isFunc[6][i]) SetFunc(matrix, isFunc, 6, i, dark);
            if (!isFunc[i][6]) SetFunc(matrix, isFunc, i, 6, dark);
        }
    }

    private static void PlaceAlignmentPattern(bool[][] matrix, bool[][] isFunc, int size)
    {
        int centerRow = size - 7;
        int centerCol = size - 7;

        for (int r = -2; r <= 2; r++)
        {
            for (int c = -2; c <= 2; c++)
            {
                bool dark = (Math.Abs(r) == 2 || Math.Abs(c) == 2 || (r == 0 && c == 0));
                SetFunc(matrix, isFunc, centerRow + r, centerCol + c, dark);
            }
        }
    }

    private static void ReserveFormatInfo(bool[][] isFunc, int size)
    {
        for (int i = 0; i < 9; i++)
        {
            if (i != 6) isFunc[8][i] = true;
            if (i != 6) isFunc[i][8] = true;
        }
        for (int i = 0; i < 8; i++)
        {
            isFunc[8][size - 1 - i] = true;
            isFunc[size - 1 - i][8] = true;
        }
        isFunc[size - 8][8] = true;
    }

    private static void PlaceDataBits(bool[][] matrix, bool[][] isFunc, int size, List<byte> data)
    {
        int bitIdx = 0;
        int totalBits = data.Count * 8;

        int row = size - 1;
        int col = size - 1;
        int dir = -1;

        while (col > 0)
        {
            if (col == 6) col--;

            for (int c = 0; c < 2; c++)
            {
                int currCol = col - c;
                if (!isFunc[row][currCol])
                {
                    bool bit = false;
                    if (bitIdx < totalBits)
                    {
                        int byteIdx = bitIdx / 8;
                        int bitInByte = 7 - (bitIdx % 8);
                        bit = ((data[byteIdx] >> bitInByte) & 1) == 1;
                        bitIdx++;
                    }
                    matrix[row][currCol] = bit;
                }
            }

            row += dir;
            if (row < 0 || row >= size)
            {
                dir = -dir;
                row += dir;
                col -= 2;
            }
        }
    }

    private static bool[][] ApplyMask(bool[][] matrix, bool[][] isFunc, int size, int mask)
    {
        bool[][] masked = CreateMatrix(size);
        for (int r = 0; r < size; r++)
        {
            for (int c = 0; c < size; c++)
            {
                if (isFunc[r][c])
                {
                    masked[r][c] = matrix[r][c];
                }
                else
                {
                    bool invert = mask switch
                    {
                        0 => (r + c) % 2 == 0,
                        1 => r % 2 == 0,
                        2 => c % 3 == 0,
                        3 => (r + c) % 3 == 0,
                        4 => ((r / 2) + (c / 3)) % 2 == 0,
                        5 => ((r * c) % 2) + ((r * c) % 3) == 0,
                        6 => (((r * c) % 2) + ((r * c) % 3)) % 2 == 0,
                        7 => (((r + c) % 2) + ((r * c) % 3)) % 2 == 0,
                        _ => false
                    };
                    masked[r][c] = invert ? !matrix[r][c] : matrix[r][c];
                }
            }
        }
        return masked;
    }

    private static void EmbedFormatInfo(bool[][] matrix, int size, QrErrorCorrectionLevel level, int mask)
    {
        int formatData = ((int)level << 3) | mask;
        int rem = formatData << 10;
        for (int i = 14; i >= 10; i--)
        {
            if (((rem >> i) & 1) == 1)
            {
                rem ^= (0x537 << (i - 10));
            }
        }
        int formatBits = ((formatData << 10) | rem) ^ 0x5412;

        int bit = 0;
        for (int c = 0; c <= 8; c++)
        {
            if (c == 6) continue;
            matrix[8][c] = ((formatBits >> (14 - bit)) & 1) == 1;
            bit++;
        }
        for (int r = 7; r >= 0; r--)
        {
            if (r == 6) continue;
            matrix[r][8] = ((formatBits >> (14 - bit)) & 1) == 1;
            bit++;
        }

        bit = 0;
        for (int r = size - 1; r >= size - 7; r--)
        {
            matrix[r][8] = ((formatBits >> (14 - bit)) & 1) == 1;
            bit++;
        }
        matrix[size - 8][8] = true;
        for (int c = size - 8; c < size; c++)
        {
            matrix[8][c] = ((formatBits >> (14 - bit)) & 1) == 1;
            bit++;
        }
    }

    private static int CalculatePenalty(bool[][] matrix, int size)
    {
        int penalty = 0;
        for (int r = 0; r < size; r++)
        {
            int run = 1;
            for (int c = 1; c < size; c++)
            {
                if (matrix[r][c] == matrix[r][c - 1]) run++;
                else
                {
                    if (run >= 5) penalty += 3 + (run - 5);
                    run = 1;
                }
            }
            if (run >= 5) penalty += 3 + (run - 5);
        }

        for (int c = 0; c < size; c++)
        {
            int run = 1;
            for (int r = 1; r < size; r++)
            {
                if (matrix[r][c] == matrix[r - 1][c]) run++;
                else
                {
                    if (run >= 5) penalty += 3 + (run - 5);
                    run = 1;
                }
            }
            if (run >= 5) penalty += 3 + (run - 5);
        }

        for (int r = 0; r < size - 1; r++)
        {
            for (int c = 0; c < size - 1; c++)
            {
                bool color = matrix[r][c];
                if (matrix[r + 1][c] == color && matrix[r][c + 1] == color && matrix[r + 1][c + 1] == color)
                {
                    penalty += 3;
                }
            }
        }

        return penalty;
    }

    private static void SetFunc(bool[][] matrix, bool[][] isFunc, int r, int c, bool val)
    {
        matrix[r][c] = val;
        isFunc[r][c] = true;
    }
}
