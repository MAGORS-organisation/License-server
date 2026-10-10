using FluentAssertions;
using Achilles.Domain.Enforcement;
using Xunit;

namespace Achilles.Domain.Tests;

public sealed class EbpfLoaderTests
{
    [Fact]
    public void CheckCompatibility_ReturnsValidCompatibilityRecord()
    {
        var compat = EbpfKernelLoader.CheckCompatibility();
        compat.Should().NotBeNull();
        compat.KernelRelease.Should().NotBeNullOrWhiteSpace();
        compat.RecommendedHook.Should().Contain("cgroup/connect");
    }

    [Fact]
    public void GenerateBpfSourceCode_ContainsExpectedMapsAndHook()
    {
        string cCode = EbpfKernelLoader.GenerateBpfSourceCode();
        cCode.Should().Contain("license_map");
        cCode.Should().Contain("violation_ringbuf");
        cCode.Should().Contain("SEC(\"cgroup/connect4\")");
        cCode.Should().Contain("symbolon_sock4_connect");
    }
}
