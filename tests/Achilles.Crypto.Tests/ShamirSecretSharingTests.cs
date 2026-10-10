using System.Security.Cryptography;
using System.Text;
using Achilles.Crypto.SecretSharing;
using Xunit;

namespace Achilles.Crypto.Tests;

public class ShamirSecretSharingTests
{
    [Fact]
    public void Split_And_Combine_RecoversExactSecret_ForAnyThresholdSubset()
    {
        byte[] secret = Encoding.UTF8.GetBytes("Symbolon-PQC-MasterKey-Seed-SuperSecret-2026-NIST-MLDSA");
        byte k = 3;
        byte n = 5;

        var shares = ShamirSecretSharing.Split(secret, k, n);
        Assert.Equal(n, shares.Length);

        // Test všetkých 10 kombinácií výberu 3 podielov z 5
        for (int i = 0; i < n; i++)
        {
            for (int j = i + 1; j < n; j++)
            {
                for (int m = j + 1; m < n; m++)
                {
                    SecretShare[] subset = [shares[i], shares[j], shares[m]];
                    byte[] recovered = ShamirSecretSharing.Combine(subset);
                    Assert.Equal(secret, recovered);
                }
            }
        }
    }

    [Fact]
    public void Split_And_Combine_WorksWithAllShares()
    {
        byte[] secret = RandomNumberGenerator.GetBytes(128);
        var shares = ShamirSecretSharing.Split(secret, thresholdK: 4, totalSharesN: 6);

        byte[] recovered = ShamirSecretSharing.Combine(shares);
        Assert.Equal(secret, recovered);
    }

    [Fact]
    public void Combine_ThrowsWhenFewerSharesThanThreshold()
    {
        byte[] secret = Encoding.UTF8.GetBytes("Critical-Master-Password");
        var shares = ShamirSecretSharing.Split(secret, thresholdK: 3, totalSharesN: 5);

        // Poskytneme len 2 podiely namiesto 3
        SecretShare[] tooFew = [shares[0], shares[1]];
        Assert.Throws<InvalidOperationException>(() => ShamirSecretSharing.Combine(tooFew));
    }

    [Fact]
    public void Combine_ThrowsCryptographicException_WhenShareIsTampered()
    {
        byte[] secret = Encoding.UTF8.GetBytes("Tamper-Sensitive-Payload");
        var shares = ShamirSecretSharing.Split(secret, thresholdK: 2, totalSharesN: 3);

        // Pozmeníme dáta v prvom podieli
        byte[] corruptedData = (byte[])shares[0].Data.Clone();
        corruptedData[0] ^= 0xFF;
        var tamperedShare = new SecretShare(shares[0].Index, shares[0].Threshold, shares[0].TotalShares, corruptedData, shares[0].Checksum);

        SecretShare[] set = [tamperedShare, shares[1]];
        Assert.Throws<CryptographicException>(() => ShamirSecretSharing.Combine(set));
    }

    [Fact]
    public void SecretShare_Token_And_Pem_RoundTrip()
    {
        byte[] secret = Encoding.UTF8.GetBytes("Testing-Serialization-12345");
        var shares = ShamirSecretSharing.Split(secret, thresholdK: 2, totalSharesN: 3);
        var original = shares[0];

        // 1. Token Roundtrip
        string token = original.ToToken();
        Assert.StartsWith(SecretShare.TokenPrefix, token, StringComparison.Ordinal);
        bool parsedTokenOk = SecretShare.TryParse(token, out var fromToken);
        Assert.True(parsedTokenOk);
        Assert.NotNull(fromToken);
        Assert.Equal(original.Index, fromToken.Index);
        Assert.Equal(original.Threshold, fromToken.Threshold);
        Assert.Equal(original.TotalShares, fromToken.TotalShares);
        Assert.Equal(original.Data, fromToken.Data);
        Assert.Equal(original.Checksum, fromToken.Checksum);

        // 2. PEM Roundtrip
        string pem = original.ToPem();
        Assert.Contains(SecretShare.PemHeader, pem, StringComparison.Ordinal);
        bool parsedPemOk = SecretShare.TryParse(pem, out var fromPem);
        Assert.True(parsedPemOk);
        Assert.NotNull(fromPem);
        Assert.Equal(original.Index, fromPem.Index);
        Assert.Equal(original.Data, fromPem.Data);
        Assert.Equal(original.Checksum, fromPem.Checksum);

        var fromPemDirect = SecretShare.Parse(pem);
        Assert.Equal(original.Index, fromPemDirect.Index);
    }

    [Fact]
    public void Split_And_Combine_RealEs256PrivateKey()
    {
        // Vygenerovanie skutočného ES256 kľúča
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] privateKeyPkcs8 = ecdsa.ExportPkcs8PrivateKey();

        // Rozdelenie na 3-of-5 podielov
        var shares = ShamirSecretSharing.Split(privateKeyPkcs8, thresholdK: 3, totalSharesN: 5);

        // Rekonštrukcia pomocou podielov 1, 3, 5
        SecretShare[] recoverySet = [shares[0], shares[2], shares[4]];
        byte[] recoveredBytes = ShamirSecretSharing.Combine(recoverySet);
        Assert.Equal(privateKeyPkcs8, recoveredBytes);

        // Overenie, že zrekonštruovaný kľúč je 100% funkčný na podpisovanie a overovanie
        using var restoredEcdsa = ECDsa.Create();
        restoredEcdsa.ImportPkcs8PrivateKey(recoveredBytes, out _);

        byte[] testData = "Symbolon-Disaster-Recovery-Validation"u8.ToArray();
        byte[] signature = restoredEcdsa.SignData(testData, HashAlgorithmName.SHA256);

        // Pôvodný verejný kľúč overí podpis vygenerovaný zrekonštruovaným kľúčom
        bool valid = ecdsa.VerifyData(testData, signature, HashAlgorithmName.SHA256);
        Assert.True(valid);
    }
}
