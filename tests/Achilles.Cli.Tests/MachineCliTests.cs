using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Achilles.Cli.Tests;

public sealed class MachineCliTests
{
    [Fact]
    public async Task MachineHelp_ReturnsZero()
    {
        int exitCode = await Program.Main(["machine", "--help"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task Machine_WithoutArgs_ReturnsZero()
    {
        int exitCode = await Program.Main(["machine"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task MachineFingerprint_Json_ReturnsZero()
    {
        int exitCode = await Program.Main(["machine", "fingerprint", "--json"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task MachineFingerprint_WithLicenseSalt_ReturnsZero()
    {
        int exitCode = await Program.Main(["machine", "fingerprint", "--license", "SYM-TEST-SALT"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task MachineTestMatch_MatchingFiles_ReturnsZero()
    {
        string tmp1 = Path.GetTempFileName();
        string tmp2 = Path.GetTempFileName();
        try
        {
            var fp1 = new
            {
                components = new Dictionary<string, string>
                {
                    ["machineId"] = "m-1234",
                    ["cpu"] = "Intel Core i9",
                    ["disk"] = "SN-5678"
                }
            };
            var fp2 = new
            {
                components = new Dictionary<string, string>
                {
                    ["machineId"] = "m-1234",
                    ["cpu"] = "Intel Core i9",
                    ["disk"] = "SN-9999" // 2 of 3 match => match-most passes
                }
            };

            await File.WriteAllTextAsync(tmp1, JsonSerializer.Serialize(fp1));
            await File.WriteAllTextAsync(tmp2, JsonSerializer.Serialize(fp2));

            int exitCode = await Program.Main(["machine", "test-match", "--stored", tmp1, "--current", tmp2, "--strategy", "match-most"]);
            exitCode.Should().Be(0);
        }
        finally
        {
            if (File.Exists(tmp1)) File.Delete(tmp1);
            if (File.Exists(tmp2)) File.Delete(tmp2);
        }
    }

    [Fact]
    public async Task MachineTestMatch_MismatchFiles_ReturnsNonZero()
    {
        string tmp1 = Path.GetTempFileName();
        string tmp2 = Path.GetTempFileName();
        try
        {
            var fp1 = new
            {
                components = new Dictionary<string, string>
                {
                    ["machineId"] = "m-1234",
                    ["cpu"] = "Intel Core i9",
                    ["disk"] = "SN-5678"
                }
            };
            var fp2 = new
            {
                components = new Dictionary<string, string>
                {
                    ["machineId"] = "m-DIFFERENT",
                    ["cpu"] = "AMD Ryzen",
                    ["disk"] = "SN-DIFFERENT"
                }
            };

            await File.WriteAllTextAsync(tmp1, JsonSerializer.Serialize(fp1));
            await File.WriteAllTextAsync(tmp2, JsonSerializer.Serialize(fp2));

            int exitCode = await Program.Main(["machine", "test-match", "--stored", tmp1, "--current", tmp2, "--strategy", "match-all"]);
            exitCode.Should().Be(2);
        }
        finally
        {
            if (File.Exists(tmp1)) File.Delete(tmp1);
            if (File.Exists(tmp2)) File.Delete(tmp2);
        }
    }

    [Fact]
    public async Task Machine_UnknownCommand_ReturnsNonZero()
    {
        int exitCode = await Program.Main(["machine", "invalid-cmd"]);
        exitCode.Should().Be(1);
    }
}
