using System.Security.Cryptography;

namespace Symbolon.Crypto.SecretSharing;

/// <summary>
/// Implementácia Shamir's Secret Sharing (SSS) nad konečným poľom GF(2^8) pre rozdelenie
/// a bezpečnú rekonštrukciu kryptografických kľúčov a citlivých dát (k-of-n threshold scheme).
/// </summary>
public static class ShamirSecretSharing
{
    /// <summary>
    /// Rozdelí tajomstvo do n podielov, pričom na rekonštrukciu je potrebných aspoň k ľubovoľných podielov.
    /// </summary>
    /// <param name="secret">Dáta tajomstva (ľubovoľná dĺžka bajtov).</param>
    /// <param name="thresholdK">Prah potrebný na rekonštrukciu (2 &lt;= k &lt;= n).</param>
    /// <param name="totalSharesN">Celkový počet vygenerovaných podielov (k &lt;= n &lt;= 255).</param>
    /// <returns>Pole n podielov tajomstva.</returns>
    public static SecretShare[] Split(ReadOnlySpan<byte> secret, byte thresholdK, byte totalSharesN)
    {
        if (secret.IsEmpty)
        {
            throw new ArgumentException("Tajomstvo nesmie byť prázdne.", nameof(secret));
        }

        if (thresholdK < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(thresholdK), "Prah k musí byť aspoň 2.");
        }

        if (totalSharesN < thresholdK)
        {
            throw new ArgumentOutOfRangeException(nameof(totalSharesN), "Celkový počet podielov n nesmie byť menší ako prah k.");
        }

        // 4-bajtový kontrolný súčet integrity (SHA-256)
        byte[] fullHash = SHA256.HashData(secret);
        byte[] checksum = new byte[4];
        Buffer.BlockCopy(fullHash, 0, checksum, 0, 4);

        int secretLen = secret.Length;
        byte[][] shareBuffers = new byte[totalSharesN][];
        for (int i = 0; i < totalSharesN; i++)
        {
            shareBuffers[i] = new byte[secretLen];
        }

        // Polynóm stupňa (thresholdK - 1): P(x) = c0 + c1*x + c2*x^2 + ... + c_{k-1}*x^{k-1}
        // c0 je samotný bajt tajomstva, c1 .. c_{k-1} sú kryptograficky náhodné bajty
        byte[] coeffBuffer = new byte[thresholdK];

        for (int byteIdx = 0; byteIdx < secretLen; byteIdx++)
        {
            coeffBuffer[0] = secret[byteIdx];
            RandomNumberGenerator.Fill(coeffBuffer.AsSpan(1));

            for (byte x = 1; x <= totalSharesN; x++)
            {
                shareBuffers[x - 1][byteIdx] = GaloisField256.EvaluatePolynomial(coeffBuffer, x);
            }
        }

        var result = new SecretShare[totalSharesN];
        for (byte x = 1; x <= totalSharesN; x++)
        {
            result[x - 1] = new SecretShare(x, thresholdK, totalSharesN, shareBuffers[x - 1], checksum);
        }

        return result;
    }

    /// <summary>
    /// Zrekonštruuje pôvodné tajomstvo z aspoň k unikátnych podielov pomocou Lagrangeovej interpolácie.
    /// </summary>
    /// <param name="shares">Kolekcia podielov (aspoň k kusov).</param>
    /// <returns>Pôvodné bajty tajomstva.</returns>
    public static byte[] Combine(IReadOnlyCollection<SecretShare> shares)
    {
        ArgumentNullException.ThrowIfNull(shares);

        if (shares.Count == 0)
        {
            throw new ArgumentException("Neboli poskytnuté žiadne podiely tajomstva.", nameof(shares));
        }

        // Deduplikácia podľa indexu podielu
        var distinctShares = new Dictionary<byte, SecretShare>();
        foreach (var share in shares)
        {
            distinctShares.TryAdd(share.Index, share);
        }

        var first = shares.First();
        byte thresholdK = first.Threshold;
        byte totalSharesN = first.TotalShares;
        int dataLength = first.Data.Length;
        byte[] expectedChecksum = first.Checksum;

        if (distinctShares.Count < thresholdK)
        {
            throw new InvalidOperationException(
                $"Nedostatočný počet unikátnych podielov. Požadovaných: {thresholdK}, k dispozícii: {distinctShares.Count}.");
        }

        // Validácia konzistencie všetkých podielov
        foreach (var share in distinctShares.Values)
        {
            if (share.Threshold != thresholdK || share.TotalShares != totalSharesN)
            {
                throw new ArgumentException("Podiely pochádzajú z nekompatibilných parametrov rozdelenia (k/n nesedí).", nameof(shares));
            }

            if (share.Data.Length != dataLength)
            {
                throw new ArgumentException("Dĺžka dát v podieloch sa nezhoduje.", nameof(shares));
            }

            if (!share.Checksum.AsSpan().SequenceEqual(expectedChecksum))
            {
                throw new ArgumentException("Kontrolný súčet v podieloch sa nezhoduje (podiely nepochádzajú z rovnakého tajomstva).", nameof(shares));
            }
        }

        // Výber prvých k unikátnych podielov
        var selectedShares = distinctShares.Values.Take(thresholdK).ToArray();
        byte[] reconstructed = new byte[dataLength];

        // Predvýpočet Lagrangeových bázových hodnôt L_i(0)
        // L_i(0) = PROD_{j != i} [ x_j / (x_i XOR x_j) ]
        byte[] lagrangeWeights = new byte[thresholdK];
        for (int i = 0; i < thresholdK; i++)
        {
            byte xi = selectedShares[i].Index;
            byte weight = 1;

            for (int j = 0; j < thresholdK; j++)
            {
                if (i == j)
                {
                    continue;
                }

                byte xj = selectedShares[j].Index;
                byte denom = GaloisField256.Add(xi, xj); // xi ^ xj
                byte ratio = GaloisField256.Divide(xj, denom);
                weight = GaloisField256.Multiply(weight, ratio);
            }

            lagrangeWeights[i] = weight;
        }

        // Rekonštrukcia každého bajtu: S[b] = XOR_{i=0..k-1} [ y_i[b] * L_i(0) ]
        for (int b = 0; b < dataLength; b++)
        {
            byte secretByte = 0;
            for (int i = 0; i < thresholdK; i++)
            {
                byte term = GaloisField256.Multiply(selectedShares[i].Data[b], lagrangeWeights[i]);
                secretByte = GaloisField256.Add(secretByte, term);
            }
            reconstructed[b] = secretByte;
        }

        // Overenie kontrolného súčtu integrity
        byte[] computedHash = SHA256.HashData(reconstructed);
        if (!computedHash.AsSpan(0, 4).SequenceEqual(expectedChecksum))
        {
            throw new CryptographicException(
                "Rekonštrukcia zlyhala: Kontrolný súčet nesúhlasí. Niektorý z podielov je pravdepodobne poškodený alebo pozmenený.");
        }

        return reconstructed;
    }
}
