using Symbolon.Crypto.SecretSharing;
using Xunit;

namespace Symbolon.Crypto.Tests;

public class GaloisField256Tests
{
    [Fact]
    public void Add_And_Subtract_BehaveAsXor()
    {
        byte a = 0x57;
        byte b = 0x83;

        Assert.Equal((byte)(a ^ b), GaloisField256.Add(a, b));
        Assert.Equal(GaloisField256.Add(a, b), GaloisField256.Subtract(a, b));
        Assert.Equal(a, GaloisField256.Add(a, 0));
        Assert.Equal(0, GaloisField256.Add(a, a));
    }

    [Fact]
    public void Multiply_FollowsFieldAxioms()
    {
        for (int i = 0; i <= 255; i++)
        {
            byte a = (byte)i;
            Assert.Equal(a, GaloisField256.Multiply(a, 1));
            Assert.Equal(0, GaloisField256.Multiply(a, 0));
        }

        byte x = 0x53;
        byte y = 0xCA;
        byte z = 0x19;

        // Komutatívnosť
        Assert.Equal(GaloisField256.Multiply(x, y), GaloisField256.Multiply(y, x));

        // Asociatívnosť
        byte xy_z = GaloisField256.Multiply(GaloisField256.Multiply(x, y), z);
        byte x_yz = GaloisField256.Multiply(x, GaloisField256.Multiply(y, z));
        Assert.Equal(xy_z, x_yz);

        // Distributívnosť: x * (y + z) = (x * y) + (x * z)
        byte left = GaloisField256.Multiply(x, GaloisField256.Add(y, z));
        byte right = GaloisField256.Add(GaloisField256.Multiply(x, y), GaloisField256.Multiply(x, z));
        Assert.Equal(left, right);
    }

    [Fact]
    public void Inverse_And_Division_AreCorrectForAllNonZeroElements()
    {
        Assert.Throws<DivideByZeroException>(() => GaloisField256.Inverse(0));
        Assert.Throws<DivideByZeroException>(() => GaloisField256.Divide(0x42, 0));

        for (int i = 1; i <= 255; i++)
        {
            byte a = (byte)i;
            byte inv = GaloisField256.Inverse(a);

            Assert.Equal(1, GaloisField256.Multiply(a, inv));

            for (int j = 1; j <= 50; j++)
            {
                byte num = (byte)j;
                byte div = GaloisField256.Divide(num, a);
                Assert.Equal(num, GaloisField256.Multiply(div, a));
            }
        }
    }

    [Fact]
    public void EvaluatePolynomial_EvaluatesHornerCorrectly()
    {
        // P(x) = 5 + 7*x + 9*x^2
        byte[] coeffs = [5, 7, 9];

        // Pri x = 0 je P(0) = c0 = 5
        Assert.Equal(5, GaloisField256.EvaluatePolynomial(coeffs, 0));

        // Pri x = 1: P(1) = 5 ^ 7 ^ 9 = 11
        byte expectedAt1 = GaloisField256.Add(GaloisField256.Add(5, 7), 9);
        Assert.Equal(expectedAt1, GaloisField256.EvaluatePolynomial(coeffs, 1));
    }
}
