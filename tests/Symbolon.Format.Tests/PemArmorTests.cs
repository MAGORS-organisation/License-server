using System.Text;
using FluentAssertions;
using Xunit;

namespace Symbolon.Format.Tests;

public sealed class PemArmorTests
{
    [Fact]
    public void WrapAndUnwrap_MatchesOriginalBytes()
    {
        byte[] original = Encoding.UTF8.GetBytes("This is raw binary content to wrap in PEM.");
        string pem = PemArmor.Wrap("TEST LABEL", original);

        pem.Should().StartWith("-----BEGIN TEST LABEL-----\n");
        pem.Should().EndWith("-----END TEST LABEL-----\n");

        bool success = PemArmor.TryUnwrap(pem, "TEST LABEL", out byte[]? unwrapped);
        success.Should().BeTrue();
        unwrapped.Should().Equal(original);
    }

    [Fact]
    public void Unwrap_MismatchedLabel_ReturnsFalse()
    {
        byte[] original = Encoding.UTF8.GetBytes("Content");
        string pem = PemArmor.Wrap("LABEL A", original);

        bool success = PemArmor.TryUnwrap(pem, "LABEL B", out _);
        success.Should().BeFalse();
    }

    [Fact]
    public void Unwrap_MalformedBase64_ReturnsFalse()
    {
        string malformed = "-----BEGIN TEST-----\nNotBase64!@#$%\n-----END TEST-----\n";
        bool success = PemArmor.TryUnwrap(malformed, "TEST", out _);
        success.Should().BeFalse();
    }
}
