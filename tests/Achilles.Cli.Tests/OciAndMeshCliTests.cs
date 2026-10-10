using System.Text.Json;
using FluentAssertions;
using Achilles.Crypto;
using Achilles.Format.Attestation;
using Xunit;

namespace Achilles.Cli.Tests;

public sealed class OciAndMeshCliTests
{
    [Fact]
    public async Task Oci_Pack_And_Verify_Workflow_Succeeds()
    {
        string keyFile = Path.Combine(Path.GetTempPath(), $"key_{Guid.NewGuid():N}.json");
        string jwksFile = Path.Combine(Path.GetTempPath(), $"jwks_{Guid.NewGuid():N}.json");
        string licenseFile = Path.Combine(Path.GetTempPath(), $"lic_{Guid.NewGuid():N}.symlic");
        string bundleFile = Path.Combine(Path.GetTempPath(), $"bundle_{Guid.NewGuid():N}.tar");

        try
        {
            // 1. Issue license with matching exported JWKS
            int issueCode = await Program.Main([
                "license", "issue",
                "--customer", "CUST-OCI-01",
                "--seats", "10",
                "--out", licenseFile,
                "--jwks-out", jwksFile
            ]);
            issueCode.Should().Be(0);
            File.Exists(licenseFile).Should().BeTrue();
            File.Exists(jwksFile).Should().BeTrue();

            // 3. OCI Pack
            int packCode = await Program.Main([
                "oci", "pack",
                "--license", licenseFile,
                "--jwks", jwksFile,
                "--customer", "CUST-OCI-01",
                "--product", "acme-app",
                "--out", bundleFile
            ]);
            packCode.Should().Be(0);
            File.Exists(bundleFile).Should().BeTrue();

            // 4. OCI Verify
            int verifyCode = await Program.Main(["oci", "verify", bundleFile]);
            verifyCode.Should().Be(0);
        }
        finally
        {
            if (File.Exists(keyFile)) try { File.Delete(keyFile); } catch { /* ignore */ }
            if (File.Exists(jwksFile)) try { File.Delete(jwksFile); } catch { /* ignore */ }
            if (File.Exists(licenseFile)) try { File.Delete(licenseFile); } catch { /* ignore */ }
            if (File.Exists(bundleFile)) try { File.Delete(bundleFile); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task Mesh_Status_Command_Succeeds()
    {
        int exitCode = await Program.Main(["mesh", "status", "--node", "rly-test-kosice", "--seats", "15"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task Attestation_Verify_Command_Succeeds()
    {
        string keyFile = Path.Combine(Path.GetTempPath(), $"aik_{Guid.NewGuid():N}.json");
        string quoteFile = Path.Combine(Path.GetTempPath(), $"quote_{Guid.NewGuid():N}.json");

        try
        {
            using var aik = Es256SignatureProvider.GenerateKey("aik-cli-test-01");
            var jwk = aik.ExportPublicJwk();
            string nonce = "challenge_nonce_cli_123";
            var quote = TpmQuoteGenerator.CreateQuote(aik, EnclaveType.Tpm20, nonce, [0, 1, 7]);

            await File.WriteAllTextAsync(keyFile, JsonSerializer.Serialize(jwk));
            await File.WriteAllTextAsync(quoteFile, JsonSerializer.Serialize(quote));

            int verifyCode = await Program.Main([
                "attestation", "verify",
                "--quote", quoteFile,
                "--key", keyFile,
                "--nonce", nonce
            ]);

            verifyCode.Should().Be(0);
        }
        finally
        {
            if (File.Exists(keyFile)) try { File.Delete(keyFile); } catch { /* ignore */ }
            if (File.Exists(quoteFile)) try { File.Delete(quoteFile); } catch { /* ignore */ }
        }
    }
}
