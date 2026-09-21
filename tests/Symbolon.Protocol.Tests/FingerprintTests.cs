using FluentAssertions;
using Xunit;

namespace Symbolon.Protocol.Tests;

public sealed class FingerprintTests
{
    [Fact]
    public void Canonicalize_SortsKeysAlphabetically_AndNormalizes()
    {
        var components = new Dictionary<string, string>
        {
            ["machineId"] = "MAC-12345",
            ["board"] = "BOARD-XYZ",
            ["cpu"] = "CPU-INTEL"
        };

        string canonical = FingerprintHelper.Canonicalize(components);

        // Expect BOARD, then CPU, then MACHINEID sorted
        canonical.Should().Be("BOARD=BOARD-XYZ\nCPU=CPU-INTEL\nMACHINEID=MAC-12345\n");
    }

    [Fact]
    public void ComputeHash_ProducesExpectedSha256Format()
    {
        var components = new Dictionary<string, string>
        {
            ["machineId"] = "TEST-PC-1"
        };

        string hash = FingerprintHelper.ComputeHash(components);

        hash.Should().StartWith("sha256:");
        hash.Length.Should().Be(7 + 64); // "sha256:" (7) + 64 hex chars
    }
}
