using FluentAssertions;
using Xunit;

namespace Achilles.Format.Tests;

public sealed class LicenseKeyTests
{
    [Fact]
    public void Generate_DefaultPrefix_ProducesValidKey()
    {
        var key = LicenseKey.Generate();

        key.Prefix.Should().Be("SYM");
        key.Entropy.Length.Should().Be(20);
        key.Checksum.Length.Should().Be(5);
        key.Canonical.Should().MatchRegex(@"^SYM-[0-9A-HJKMNP-TV-Z]{5}-[0-9A-HJKMNP-TV-Z]{5}-[0-9A-HJKMNP-TV-Z]{5}-[0-9A-HJKMNP-TV-Z]{5}-[0-9A-HJKMNP-TV-Z]{5}$");

        bool parsed = LicenseKey.TryParse(key.Canonical, out var parsedKey, out string? error);
        parsed.Should().BeTrue();
        error.Should().BeNull();
        parsedKey!.Canonical.Should().Be(key.Canonical);
    }

    [Fact]
    public void Generate_CustomPrefix_ProducesValidKey()
    {
        var key = LicenseKey.Generate("ACME");

        key.Prefix.Should().Be("ACME");
        key.Canonical.Should().StartWith("ACME-");

        bool parsed = LicenseKey.TryParse(key.Canonical, out var parsedKey, out _);
        parsed.Should().BeTrue();
        parsedKey!.Prefix.Should().Be("ACME");
    }

    [Fact]
    public void TryParse_ToleratesLowercaseAndMissingDashes_AndNormalizesLetters()
    {
        var original = LicenseKey.Generate();
        // Replace dashes with spaces, make lowercase, replace '1' with 'I' and '0' with 'O'
        string mangled = original.Canonical.ToLowerInvariant().Replace('-', ' ');
        mangled = mangled.Replace('1', 'I').Replace('0', 'O');

        bool parsed = LicenseKey.TryParse(mangled, out var parsedKey, out string? error);
        parsed.Should().BeTrue();
        error.Should().BeNull();
        parsedKey!.Canonical.Should().Be(original.Canonical);
    }

    [Fact]
    public void TryParse_CorruptedChecksum_ReturnsChecksumMismatch()
    {
        var key = LicenseKey.Generate();
        // Change the last character
        char lastChar = key.Canonical[^1];
        char corruptedChar = lastChar == '9' ? '8' : '9';
        string corruptedKey = string.Concat(key.Canonical.AsSpan(0, key.Canonical.Length - 1), corruptedChar.ToString());

        bool parsed = LicenseKey.TryParse(corruptedKey, out var parsedKey, out string? error);
        parsed.Should().BeFalse();
        parsedKey.Should().BeNull();
        error.Should().Be("checksum-mismatch");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("SYM-123")] // too short
    [InlineData("SYM-INVALIDCHARWITHU-XXXXX-XXXXX-XXXXX-XXXXX")] // contains 'U'
    public void TryParse_InvalidInput_ReturnsError(string invalidInput)
    {
        bool parsed = LicenseKey.TryParse(invalidInput, out _, out string? error);
        parsed.Should().BeFalse();
        error.Should().NotBeNullOrWhiteSpace();
    }
}
