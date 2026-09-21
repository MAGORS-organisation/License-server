namespace Symbolon.Format;

/// <summary>
/// CRC-32C (Castagnoli, polynomial 0x82F63B78 reflected / 0x1EDC6F41) implementation.
/// Conforms to RFC 3720 and Symbolon KEY-3 specification.
/// </summary>
public static class Crc32C
{
    private const uint Polynomial = 0x82F63B78;
    private static readonly uint[] Table = InitializeTable();

    private static uint[] InitializeTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            uint crc = i;
            for (int j = 0; j < 8; j++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ Polynomial : (crc >> 1);
            }
            table[i] = crc;
        }
        return table;
    }

    /// <summary>
    /// Computes the 32-bit CRC-32C value over the provided byte span.
    /// </summary>
    public static uint Compute(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        for (int i = 0; i < data.Length; i++)
        {
            byte index = (byte)((crc ^ data[i]) & 0xFF);
            crc = Table[index] ^ (crc >> 8);
        }
        return crc ^ 0xFFFFFFFF;
    }
}
