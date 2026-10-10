using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Symbolon.Cli.Tests;

public sealed class TenantCommandsTests
{
    [Fact]
    public async Task TenantHelp_ReturnsZero()
    {
        int exitCode = await Program.Main(["tenant", "--help"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task BrandingHelp_ReturnsZero()
    {
        int exitCode = await Program.Main(["branding", "--help"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task TenantMissingTenantId_ReturnsOne()
    {
        int exitCode = await Program.Main(["tenant", "branding"]);
        exitCode.Should().Be(1);
    }

    [Fact]
    public async Task TenantQuotasMissingTenantId_ReturnsOne()
    {
        int exitCode = await Program.Main(["tenant", "quotas"]);
        exitCode.Should().Be(1);
    }
}
