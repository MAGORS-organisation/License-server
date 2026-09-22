namespace Symbolon.Crypto.SecretSharing;

/// <summary>
/// Aritmetika konečného poľa Galois Field GF(2^8) s ireducibilným polynómom x^8 + x^4 + x^3 + x + 1 (0x11B).
/// Používané pre bezpečné delenie tajomstiev Shamir's Secret Sharing a Reed-Solomon kódy.
/// </summary>
public static class GaloisField256
{
    private const int Polynomial = 0x11B;

    private static readonly byte[] ExpTable = new byte[512];
    private static readonly byte[] LogTable = new byte[256];
    private static readonly byte[] InverseTable = new byte[256];

    static GaloisField256()
    {
        // Generovanie tabuliek násobenia a inverzie pomocou Russian Peasant algoritmu
        for (int i = 1; i <= 255; i++)
        {
            byte a = (byte)i;
            for (int j = 1; j <= 255; j++)
            {
                byte b = (byte)j;
                if (RussianPeasantMultiply(a, b) == 1)
                {
                    InverseTable[a] = b;
                    break;
                }
            }
        }

        // Generovanie exp a log tabuliek pomocou generátora alpha = 3
        int x = 1;
        for (int i = 0; i < 255; i++)
        {
            ExpTable[i] = (byte)x;
            ExpTable[i + 255] = (byte)x;
            LogTable[x] = (byte)i;
            x = RussianPeasantMultiply((byte)x, 3);
        }
    }

    /// <summary>
    /// Sčítanie v GF(2^8) — ekvivalentné bitovému XOR.
    /// </summary>
    public static byte Add(byte a, byte b) => (byte)(a ^ b);

    /// <summary>
    /// Odčítanie v GF(2^8) — v poli charakteristiky 2 je identické so sčítaním (XOR).
    /// </summary>
    public static byte Subtract(byte a, byte b) => (byte)(a ^ b);

    /// <summary>
    /// Násobenie dvoch prvkov v GF(2^8).
    /// </summary>
    public static byte Multiply(byte a, byte b)
    {
        if (a == 0 || b == 0)
        {
            return 0;
        }

        int logSum = LogTable[a] + LogTable[b];
        return ExpTable[logSum];
    }

    /// <summary>
    /// Výpočet multiplikatívnej inverzie a^(-1) v GF(2^8).
    /// </summary>
    public static byte Inverse(byte a)
    {
        if (a == 0)
        {
            throw new DivideByZeroException("V konečnom poli GF(2^8) nie je možné invertovať nulu.");
        }

        return InverseTable[a];
    }

    /// <summary>
    /// Delenie dvoch prvkov v GF(2^8): a / b = a * b^(-1).
    /// </summary>
    public static byte Divide(byte a, byte b)
    {
        if (b == 0)
        {
            throw new DivideByZeroException("V konečnom poli GF(2^8) nie je možné deliť nulou.");
        }

        if (a == 0)
        {
            return 0;
        }

        return Multiply(a, InverseTable[b]);
    }

    /// <summary>
    /// Vyhodnotenie polynómu P(x) = c0 + c1*x + c2*x^2 + ... + cd*x^d v bode x pomocou Hornerovej schémy.
    /// Koeficienty sú usporiadané od najnižšieho rádu (c0) po najvyšší (cd).
    /// </summary>
    public static byte EvaluatePolynomial(ReadOnlySpan<byte> coefficients, byte x)
    {
        if (coefficients.IsEmpty)
        {
            return 0;
        }

        if (x == 0)
        {
            return coefficients[0];
        }

        byte result = coefficients[^1];
        for (int i = coefficients.Length - 2; i >= 0; i--)
        {
            result = Add(Multiply(result, x), coefficients[i]);
        }

        return result;
    }

    private static byte RussianPeasantMultiply(byte a, byte b)
    {
        byte p = 0;
        byte currA = a;
        byte currB = b;

        while (currA != 0 && currB != 0)
        {
            if ((currB & 1) != 0)
            {
                p = (byte)(p ^ currA);
            }

            bool highBit = (currA & 0x80) != 0;
            currA = (byte)(currA << 1);
            if (highBit)
            {
                currA = (byte)(currA ^ (Polynomial & 0xFF));
            }

            currB = (byte)(currB >> 1);
        }

        return p;
    }
}
