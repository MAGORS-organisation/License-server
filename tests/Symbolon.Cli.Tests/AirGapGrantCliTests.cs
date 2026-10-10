using FluentAssertions;
using Symbolon.Cli.Commands;
using Xunit;

namespace Symbolon.Cli.Tests;

public sealed class AirGapGrantCliTests
{
    [Fact]
    public async Task HandleGrant_Qr_WithData_Succeeds()
    {
        string[] args = ["qr", "--data", "SYM1-DEMO-AIRGAP-KEY-999", "--title", "AIR-GAP TEST KEY"];
        int exitCode = await GrantCommands.HandleGrantAsync(args);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task HandleGrant_Qr_WithFile_Succeeds()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"airgap_test_{Guid.NewGuid():N}.symreq");
        try
        {
            await File.WriteAllTextAsync(tempFile, "SYMBOLON AIR-GAP OFFLINE REQUEST PAYLOAD TEST 12345");
            string[] args = ["qr", "--in", tempFile];
            int exitCode = await GrantCommands.HandleGrantAsync(args);
            exitCode.Should().Be(0);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task HandleGrant_UsbDigest_CreateAndVerifyManifest_DetectsTampering()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"usb_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            string reqFile = Path.Combine(tempDir, "request.symreq");
            string licFile = Path.Combine(tempDir, "license.symlic");

            await File.WriteAllTextAsync(reqFile, "test-req-payload-1");
            await File.WriteAllTextAsync(licFile, "test-lic-payload-2");

            // 1. Create Manifest
            int createCode = await GrantCommands.HandleGrantAsync(["usb-digest", "--dir", tempDir, "--create-manifest"]);
            createCode.Should().Be(0);

            string manifestPath = Path.Combine(tempDir, "manifest.sha256");
            File.Exists(manifestPath).Should().BeTrue();
            string manifestContent = await File.ReadAllTextAsync(manifestPath);
            manifestContent.Should().Contain("request.symreq");
            manifestContent.Should().Contain("license.symlic");

            // 2. Verify Manifest (clean state)
            int verifyCode = await GrantCommands.HandleGrantAsync(["usb-digest", "--dir", tempDir, "--verify"]);
            verifyCode.Should().Be(0);

            // 3. Tamper with license file
            await File.AppendAllTextAsync(licFile, "TAMPERED_BYTES");

            // 4. Verify Manifest (must detect tampering)
            int tamperedCode = await GrantCommands.HandleGrantAsync(["usb-digest", "--dir", tempDir, "--verify"]);
            tamperedCode.Should().Be(1, "Tampered file must fail verification");
        }
        finally
        {
            if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true);
        }
    }
}
