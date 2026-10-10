using FluentAssertions;
using Achilles.Format;
using Xunit;

namespace Achilles.Format.Tests;

public sealed class QrCodeEncoderTests
{
    [Fact]
    public void Encode_ShortKey_ProducesValidVersion1Matrix()
    {
        string key = "SYM1-ABCD-1234";
        bool[][] matrix = QrCodeEncoder.Encode(key, QrErrorCorrectionLevel.M);

        matrix.Should().NotBeNull();
        int size = matrix.Length;
        matrix[0].Length.Should().Be(size);
        size.Should().Be(21, "Version 1 QR code must be 21x21 modules");

        // Verify top-left finder pattern 7x7
        // (0,0) must be dark
        matrix[0][0].Should().BeTrue();
        matrix[0][6].Should().BeTrue();
        matrix[6][0].Should().BeTrue();
        matrix[6][6].Should().BeTrue();
        // Inner 3x3 at center (3,3) must be dark
        matrix[3][3].Should().BeTrue();
        // Ring around center at (1,1) must be light
        matrix[1][1].Should().BeFalse();
    }

    [Fact]
    public void Encode_LongerUrl_ScalesToAppropriateVersion()
    {
        string url = "https://symbolon.dev/v1/offline/activate?key=SYM1-TEST-KEY-1234567890&ch=nonce_123456";
        bool[][] matrix = QrCodeEncoder.Encode(url, QrErrorCorrectionLevel.L);

        int size = matrix.Length;
        size.Should().BeGreaterThan(21, "Long payload must scale to Version 2 or higher");
        // Module sizes in QR Code are 17 + 4*V: 21, 25, 29, 33, 37, 41
        (size % 4).Should().Be(1);
    }

    [Fact]
    public void RenderAscii_ProducesNonEmptyBlockOutput()
    {
        string data = "SYM1-DEMO-AIRGAP-KEY";
        string ascii = QrCodeEncoder.RenderAscii(data, QrErrorCorrectionLevel.M);

        ascii.Should().NotBeNullOrWhiteSpace();
        ascii.Should().Contain("█");
        var lines = ascii.Split(["\r\n", "\n"], StringSplitOptions.RemoveEmptyEntries);
        lines.Length.Should().BeGreaterThan(10);
    }

    [Fact]
    public void RenderRetroBox_ContainsDoubleBordersAndTitle()
    {
        string data = "SYM1-AIRGAP-DIGEST-XYZ";
        string retro = QrCodeEncoder.RenderRetroBox(data, "AIR-GAP TEST", QrErrorCorrectionLevel.M);

        retro.Should().NotBeNullOrWhiteSpace();
        retro.Should().Contain("╔");
        retro.Should().Contain("╗");
        retro.Should().Contain("╚");
        retro.Should().Contain("╝");
        retro.Should().Contain("AIR-GAP TEST");
    }

    [Fact]
    public void Encode_ExcessivePayload_ThrowsArgumentException()
    {
        string oversized = new string('A', 500);
        var act = () => QrCodeEncoder.Encode(oversized);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*too long*");
    }
}
