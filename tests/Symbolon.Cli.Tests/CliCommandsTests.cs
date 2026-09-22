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

    [Fact]
    public async Task Sbom_And_VerifyArtifact_Workflow_Succeeds()
    {
        string tempSbom = Path.Combine(Path.GetTempPath(), $"sbom_{Guid.NewGuid():N}.json");

        try
        {
            // 1. Generate CycloneDX SBOM
            int sbomCode = await Program.Main(["sbom", "--out", tempSbom]);
            sbomCode.Should().Be(0);
            File.Exists(tempSbom).Should().BeTrue();

            string json = await File.ReadAllTextAsync(tempSbom);
            json.Should().Contain("CycloneDX");
            json.Should().Contain("Symbolon.ControlPlane");

            // 2. Verify artifact
            int verifyCode = await Program.Main(["verify-artifact", tempSbom]);
            verifyCode.Should().Be(0);

            // 3. Verify with checksum
            byte[] bytes = await File.ReadAllBytesAsync(tempSbom);
            string sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));

            int checksumCode = await Program.Main(["verify-artifact", tempSbom, "--checksum", sha256]);
            checksumCode.Should().Be(0);

            // 4. Verify with invalid checksum returns non-zero
            int failCode = await Program.Main(["verify-artifact", tempSbom, "--checksum", "0000000000000000000000000000000000000000000000000000000000000000"]);
            failCode.Should().Be(2);
        }
        finally
        {
            if (File.Exists(tempSbom)) try { File.Delete(tempSbom); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task LicenseBorrowCommand_Validation_ReturnsExpectedCodes()
    {
        // 1. Missing lease parameter
        int missingLeaseCode = await Program.Main(["license", "borrow"]);
        missingLeaseCode.Should().Be(1);

        // 2. Invalid days parameter
        int invalidDaysCode = await Program.Main(["license", "borrow", "--lease", "lse_123", "--days", "0"]);
        invalidDaysCode.Should().Be(1);

        int tooManyDaysCode = await Program.Main(["license", "borrow", "--lease", "lse_123", "--days", "45"]);
        tooManyDaysCode.Should().Be(1);
    }

    [Fact]
    public async Task LicenseReturnCommand_Validation_ReturnsExpectedCodes()
    {
        // Missing lease parameter
        int missingLeaseCode = await Program.Main(["license", "return"]);
        missingLeaseCode.Should().Be(1);
    }
}
