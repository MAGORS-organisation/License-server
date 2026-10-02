using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace Symbolon.Cli.Tests;

public sealed class TransparencyCliTests
{
    [Fact]
    public async Task TransparencyHelp_ReturnsZero()
    {
        int exitCode = await Program.Main(["transparency", "--help"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task Transparency_WithoutArgs_ReturnsZero()
    {
        int exitCode = await Program.Main(["transparency"]);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task Transparency_UnknownCommand_ReturnsNonZero()
    {
        int exitCode = await Program.Main(["transparency", "nonexistent-command"]);
        exitCode.Should().Be(1);
    }

    [Fact]
    public async Task VerifyConsistency_Offline_ValidProof_ReturnsZero()
    {
        // Test offline verification CLI subcommand
        // Leaves d0, d1 -> root = HashNode(d0, d1)
        byte[] d0 = Symbolon.Domain.Transparency.MerkleTree.HashLeaf(System.Text.Encoding.UTF8.GetBytes("leaf-0"));
        byte[] d1 = Symbolon.Domain.Transparency.MerkleTree.HashLeaf(System.Text.Encoding.UTF8.GetBytes("leaf-1"));
        byte[] oldRoot = d0;
        byte[] newRoot = Symbolon.Domain.Transparency.MerkleTree.HashNode(d0, d1);

        string oldHex = Convert.ToHexStringLower(oldRoot);
        string newHex = Convert.ToHexStringLower(newRoot);
        string proofHex = Convert.ToHexStringLower(d1);

        int exitCode = await Program.Main([
            "transparency", "verify-consistency",
            "--old-size", "1",
            "--new-size", "2",
            "--old-root", oldHex,
            "--new-root", newHex,
            "--proof", proofHex
        ]);

        exitCode.Should().Be(0);
    }
}
