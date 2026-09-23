using FluentAssertions;
using Symbolon.Crypto;
using Symbolon.Format;
using Xunit;

namespace Symbolon.Cli.Tests;

public sealed class RevocationsCliTests
{
    [Fact]
    public async Task RevocationsHelp_ReturnsZero()
    {
        int exitCode = await Program.Main(["revocations", "--help"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task RevocationsInspect_ValidSymrl_ReturnsZero()
    {
        using var ecKey = Es256SignatureProvider.GenerateKey("k-crl-ec");
        using var pqcKey = MlDsaSignatureProvider.GenerateKey(System.Security.Cryptography.MLDsaAlgorithm.MLDsa65, "k-crl-pqc");

        var signer = new RevocationListSigner([ecKey, pqcKey]);
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var claims = new RevocationListClaims(
            Iss: "https://auth.symbolon.dev",
            Iat: now,
            Exp: now + 3600,
            Symrl: new RevocationPayload(
                V: 1,
                Seq: 42,
                Full: true,
                Since: null,
                Revoked:
                [
                    new RevocationItem("license", "SYM-TEST-REVOKED", now, "compromised"),
                    new RevocationItem("machine", "mach_fp_test_hash", now, "stolen")
                ]
            )
        );

        string pem = signer.Sign(claims);
        string tempSymrl = Path.Combine(Path.GetTempPath(), $"crl_{Guid.NewGuid():N}.symrl");

        try
        {
            await File.WriteAllTextAsync(tempSymrl, pem);
            int exitCode = await Program.Main(["revocations", "inspect", tempSymrl]);
            exitCode.Should().Be(0);
        }
        finally
        {
            if (File.Exists(tempSymrl))
            {
                try { File.Delete(tempSymrl); } catch { /* ignore */ }
            }
        }
    }

    [Fact]
    public async Task RevocationsInspect_NonExistentFile_ReturnsOne()
    {
        int exitCode = await Program.Main(["revocations", "inspect", "non_existent_file.symrl"]);
        exitCode.Should().Be(1);
    }
}
