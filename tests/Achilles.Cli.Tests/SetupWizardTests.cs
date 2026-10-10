using System.Buffers.Text;
using System.Text.Json;
using FluentAssertions;
using Achilles.Cli.Wizard;
using Achilles.Format;
using Xunit;

namespace Achilles.Cli.Tests;

public sealed class SetupWizardTests
{
    [Fact]
    public void ControlPlaneSetup_Creates_Valid_Configuration_And_Scripts()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"cp_setup_{Guid.NewGuid():N}");

        try
        {
            var options = new ControlPlaneConfigOptions
            {
                TargetDirectory = tempDir,
                Port = 9000,
                DatabaseType = "sqlite",
                InitialTenant = "Test Tenant",
                InitialProduct = "test-prod"
            };

            var result = ControlPlaneSetup.Configure(options);

            result.Should().NotBeNull();
            result.Port.Should().Be(9000);
            File.Exists(result.ConfigPath).Should().BeTrue();
            File.Exists(result.PublicKeyPath).Should().BeTrue();
            File.Exists(result.PrivateKeyPath).Should().BeTrue();
            File.Exists(result.WindowsScriptPath).Should().BeTrue();
            File.Exists(result.PowerShellScriptPath).Should().BeTrue();
            File.Exists(result.BashScriptPath).Should().BeTrue();

            string configJson = File.ReadAllText(result.ConfigPath);
            using var doc = JsonDocument.Parse(configJson);
            doc.RootElement.GetProperty("ConnectionStrings").GetProperty("SymbolonDb").GetString()
                .Should().Contain("symbolon_controlplane.db");
            doc.RootElement.GetProperty("Symbolon").GetProperty("Port").GetInt32()
                .Should().Be(9000);

            string script = File.ReadAllText(result.WindowsScriptPath);
            script.Should().Contain("9000");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { /* ignore */ }
            }
        }
    }

    [Fact]
    public void RelaySetup_Creates_Valid_Configuration_And_Scripts()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), $"relay_setup_{Guid.NewGuid():N}");

        try
        {
            var options = new RelayConfigOptions
            {
                TargetDirectory = tempDir,
                Port = 9001,
                UpstreamControlPlaneUrl = "http://localhost:9000"
            };

            var result = RelaySetup.Configure(options);

            result.Should().NotBeNull();
            result.Port.Should().Be(9001);
            File.Exists(result.ConfigPath).Should().BeTrue();
            File.Exists(result.WindowsScriptPath).Should().BeTrue();
            File.Exists(result.PowerShellScriptPath).Should().BeTrue();
            File.Exists(result.BashScriptPath).Should().BeTrue();
            File.Exists(result.SystemdServicePath).Should().BeTrue();

            string configJson = File.ReadAllText(result.ConfigPath);
            using var doc = JsonDocument.Parse(configJson);
            doc.RootElement.GetProperty("Database").GetProperty("ConnectionString").GetString()
                .Should().Contain("symbolon-relay.db");
            doc.RootElement.GetProperty("Relay").GetProperty("UpstreamControlPlaneUrl").GetString()
                .Should().Be("http://localhost:9000");

            string serviceContent = File.ReadAllText(result.SystemdServicePath);
            serviceContent.Should().Contain("9001");
            serviceContent.Should().Contain("Achilles On-Premise License Relay");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { /* ignore */ }
            }
        }
    }

    [Fact]
    public void LicenseWizard_Issues_And_Validates_License_Document()
    {
        string outFile = Path.Combine(Path.GetTempPath(), $"test_lic_{Guid.NewGuid():N}.symlic");

        try
        {
            var options = new LicenseWizardOptions
            {
                OutputPath = outFile,
                CustomerName = "Enterprise Corp",
                ProductCode = "cad-advanced",
                Seats = 25,
                Model = "floating",
                ValidityDays = 90
            };

            var result = LicenseWizard.Issue(options);

            result.Should().NotBeNull();
            result.LicenseKey.Should().StartWith("SYM-");
            result.Seats.Should().Be(25);
            File.Exists(outFile).Should().BeTrue();

            string pem = File.ReadAllText(outFile);
            pem.Should().StartWith("-----BEGIN SYMBOLON LICENSE-----");
            pem.TrimEnd().Should().EndWith("-----END SYMBOLON LICENSE-----");

            // Unwrap PEM and inspect JWS General JSON structure
            PemArmor.TryUnwrap(pem, "SYMBOLON LICENSE", out byte[]? rawJson).Should().BeTrue();
            rawJson.Should().NotBeNull();

            var jwsDoc = JsonSerializer.Deserialize(rawJson, AchillesJsonContext.Default.JwsGeneralJson);
            jwsDoc.Should().NotBeNull();
            jwsDoc!.Signatures.Should().HaveCountGreaterThanOrEqualTo(1);

            byte[] payloadBytes = Base64Url.DecodeFromChars(jwsDoc.Payload);
            var claims = JsonSerializer.Deserialize(payloadBytes, AchillesJsonContext.Default.LicenseClaims);

            claims.Should().NotBeNull();
            claims!.Aud.Should().Be("cad-advanced");
            claims.Symlic.License.Key.Should().Be(result.LicenseKey);
            claims.Symlic.License.Model.Should().Be("floating");
            claims.Symlic.License.Customer?.Name.Should().Be("Enterprise Corp");
            claims.Symlic.Limits.MaxSeats.Should().Be(25);
        }
        finally
        {
            if (File.Exists(outFile))
            {
                try { File.Delete(outFile); } catch { /* ignore */ }
            }
        }
    }
}
