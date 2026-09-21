using System.Text;
using FluentAssertions;
using Xunit;

namespace Symbolon.Format.Tests;

public sealed class Crc32CTests
{
    [Fact]
    public void Crc32C_StandardVector_123456789()
    {
        // Standard test vector for CRC-32C (Castagnoli, poly 0x82F63B78 reflected):
        // Input ASCII "123456789" -> 0xE3069283
        byte[] input = Encoding.ASCII.GetBytes("123456789");
        uint crc = Crc32C.Compute(input);
        crc.Should().Be(0xE3069283);
    }

    [Fact]
    public void Crc32C_EmptyInput_ReturnsZero()
    {
        uint crc = Crc32C.Compute(ReadOnlySpan<byte>.Empty);
        crc.Should().Be(0x00000000);
    }
}
