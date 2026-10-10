using FluentAssertions;
using Xunit;

namespace Achilles.Format.Tests;

public sealed class CrockfordBase32Tests
{
    [Theory]
    [InlineData("sym-4k7qt-9m2xa", "SYM4K7QT9M2XA")]
    [InlineData("  sym - 4k7qt - 9m2xa \t", "SYM4K7QT9M2XA")]
    [InlineData("i-l-o", "110")] // I->1, L->1, O->0 (KEY-6)
    [InlineData("I-L-O", "110")]
    [InlineData("abcdefghjkmnpqrstvwxyz", "ABCDEFGHJKMNPQRSTVWXYZ")]
    public void Normalize_FollowsKey6Rules(string input, string expected)
    {
        string normalized = CrockfordBase32.Normalize(input);
        normalized.Should().Be(expected);
    }

    [Fact]
    public void Crockford_AlphabetValidity()
    {
        CrockfordBase32.Alphabet.Length.Should().Be(32);
        // 'U' is deliberately omitted from Crockford Base32
        CrockfordBase32.Alphabet.Should().NotContain("U");

        CrockfordBase32.IsValidChar('U').Should().BeFalse();
        CrockfordBase32.IsValidChar('u').Should().BeFalse();
    }
}
