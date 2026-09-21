using FluentAssertions;
using Xunit;

namespace Symbolon.Cli.Tests;

public sealed class CliCommandsTests
{
    [Fact]
    public async Task KeygenCommand_ReturnsZero()
    {
        int exitCode = await Program.Main(["license", "keygen"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task DoctorCommand_ReturnsZero()
    {
        int exitCode = await Program.Main(["doctor"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task KeysGenerate_AndInspect_Workflow_Succeeds()
    {
        string keyFile = Path.Combine(Path.GetTempPath(), $"key_{Guid.NewGuid():N}.json");
        string licenseFile = Path.Combine(Path.GetTempPath(), $"lic_{Guid.NewGuid():N}.symlic");

        try
        {
            // 1. Generate ES256 key
            int genCode = await Program.Main(["keys", "generate", "--alg", "ES256", "--kid", "test-ec-1", "--out", keyFile]);
            genCode.Should().Be(0);
            File.Exists(keyFile).Should().BeTrue();

            // 2. Issue license
            int issueCode = await Program.Main(["license", "issue", "--customer", "CUST-999", "--seats", "15", "--out", licenseFile]);
            issueCode.Should().Be(0);
            File.Exists(licenseFile).Should().BeTrue();

            // 3. Inspect license
            int inspectCode = await Program.Main(["license", "inspect", licenseFile]);
            inspectCode.Should().Be(0);
        }
        finally
        {
            if (File.Exists(keyFile)) try { File.Delete(keyFile); } catch { /* ignore */ }
            if (File.Exists(licenseFile)) try { File.Delete(licenseFile); } catch { /* ignore */ }
        }
    }
}
