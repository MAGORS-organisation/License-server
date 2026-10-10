using System.IO;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Achilles.Cli.Tests;

public sealed class PqcCliTests
{
    [Fact]
    public async Task Pqc_Help_ReturnsZero()
    {
        int exitCode = await Program.Main(["pqc", "--help"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task Pqc_NoArgs_ReturnsZero()
    {
        int exitCode = await Program.Main(["pqc"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task Pqc_UnknownCommand_ReturnsOne()
    {
        int exitCode = await Program.Main(["pqc", "invalid-sub"]);
        exitCode.Should().Be(1);
    }

    [Fact]
    public async Task Pqc_Kem_Test_ReturnsZero()
    {
        int exitCode = await Program.Main(["pqc", "kem", "test"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task Pqc_Kem_Generate_OutputsJwkAndReturnsZero()
    {
        string tmpFile = Path.Combine(Path.GetTempPath(), $"kem_key_{System.Guid.NewGuid():N}.json");
        try
        {
            int exitCode = await Program.Main(["pqc", "kem", "generate", "--alg", "ML-KEM-768", "--kid", "cli-test-kem", "--out", tmpFile]);
            exitCode.Should().Be(0);
            File.Exists(tmpFile).Should().BeTrue();

            string json = await File.ReadAllTextAsync(tmpFile);
            json.Should().Contain("ML-KEM-768");
            json.Should().Contain("cli-test-kem");
        }
        finally
        {
            if (File.Exists(tmpFile)) File.Delete(tmpFile);
        }
    }

    [Fact]
    public async Task Pqc_Verify_WithPqcFile_ReturnsZero()
    {
        string tmpFile = Path.Combine(Path.GetTempPath(), $"pqc_lic_{System.Guid.NewGuid():N}.symlic");
        try
        {
            await File.WriteAllTextAsync(tmpFile, "{\"alg\":\"ML-DSA-65\",\"data\":\"sample\"}");
            int exitCode = await Program.Main(["pqc", "verify", tmpFile]);
            exitCode.Should().Be(0);
        }
        finally
        {
            if (File.Exists(tmpFile)) File.Delete(tmpFile);
        }
    }

    [Fact]
    public async Task Pqc_Verify_NonExistentFile_ReturnsOne()
    {
        int exitCode = await Program.Main(["pqc", "verify", "non-existent-file-xyz.symlic"]);
        exitCode.Should().Be(1);
    }
}
