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

    [Fact]
    public async Task KeysSplit_And_KeysCombine_Workflow_Succeeds()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"sss_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        string originalKeyFile = Path.Combine(tempDir, "master.key");
        string recoveredKeyFile = Path.Combine(tempDir, "recovered.key");
        string sharesDir = Path.Combine(tempDir, "shares");

        byte[] secret = "SYMBOLON-HIGH-SECURITY-MASTER-PQC-KEY-TEST-2026"u8.ToArray();
        await File.WriteAllBytesAsync(originalKeyFile, secret);

        try
        {
            // 1. Split into 5 shares with threshold k=3
            int splitCode = await Program.Main([
                "keys", "split",
                "--in", originalKeyFile,
                "-k", "3",
                "-n", "5",
                "--out-dir", sharesDir,
                "--format", "token"
            ]);
            splitCode.Should().Be(0);

            Directory.Exists(sharesDir).Should().BeTrue();
            var shareFiles = Directory.GetFiles(sharesDir, "*.share");
            shareFiles.Length.Should().Be(5);

            // 2. Combine using a subset of 3 shares (shares 1, 3, 5)
            string share1 = Path.Combine(sharesDir, "share_1.share");
            string share3 = Path.Combine(sharesDir, "share_3.share");
            string share5 = Path.Combine(sharesDir, "share_5.share");

            int combineCode = await Program.Main([
                "keys", "combine",
                "--shares", $"{share1},{share3},{share5}",
                "--out", recoveredKeyFile
            ]);
            combineCode.Should().Be(0);

            File.Exists(recoveredKeyFile).Should().BeTrue();
            byte[] recoveredBytes = await File.ReadAllBytesAsync(recoveredKeyFile);
            recoveredBytes.Should().Equal(secret);

            // 3. Combine with insufficient shares (only 2 shares when k=3) must fail
            string failRecoveredFile = Path.Combine(tempDir, "fail.key");
            int failCode = await Program.Main([
                "keys", "combine",
                "--shares", $"{share1},{share3}",
                "--out", failRecoveredFile
            ]);
            failCode.Should().Be(1);
            File.Exists(failRecoveredFile).Should().BeFalse();
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { /* ignore */ }
            }
        }
    }

    [Fact]
    public async Task TokensCommand_HelpAndValidation_ReturnsExpectedCodes()
    {
        // 1. Calling tokens with no subcommands prints help and returns 0
        int helpCode = await Program.Main(["tokens"]);
        helpCode.Should().Be(0);

        // 2. Calling tokens with unknown subcommand fails with 1
        int unknownCode = await Program.Main(["tokens", "unknown-action"]);
        unknownCode.Should().Be(1);

        // 3. Calling tokens create without required options fails with 1
        int createFailCode = await Program.Main(["tokens", "create"]);
        createFailCode.Should().Be(1);

        // 4. Calling tokens set-rate without feature fails with 1
        int setRateFailCode = await Program.Main(["tokens", "set-rate"]);
        setRateFailCode.Should().Be(1);
    }
}

