using System.Security.Cryptography;
using System.Text;
using FluentAssertions;
using Symbolon.Crypto;
using Xunit;

namespace Symbolon.Crypto.Tests;

public sealed class MerkleTreeHelperTests
{
    [Fact]
    public void ComputeRoot_EmptyList_Returns32ZeroBytes()
    {
        var root = MerkleTreeHelper.ComputeRoot([]);
        root.Should().HaveCount(32);
        root.Should().AllBeEquivalentTo((byte)0);
    }

    [Fact]
    public void ComputeRoot_SingleLeaf_ReturnsLeafClone()
    {
        byte[] leaf = SHA256.HashData(Encoding.UTF8.GetBytes("event_1"));
        var root = MerkleTreeHelper.ComputeRoot([leaf]);
        root.Should().BeEquivalentTo(leaf);
    }

    [Fact]
    public void GenerateProof_AndVerifyProof_SucceedsForEveryLeaf()
    {
        int count = 9; // Odd count to test duplication of last element
        var leaves = Enumerable.Range(0, count)
            .Select(i => (ReadOnlyMemory<byte>)SHA256.HashData(Encoding.UTF8.GetBytes($"audit_record_{i}")))
            .ToList();

        byte[] root = MerkleTreeHelper.ComputeRoot(leaves);

        for (int i = 0; i < count; i++)
        {
            var proof = MerkleTreeHelper.GenerateProof(leaves, i);
            proof.Should().NotBeNull();
            proof.RootHash.ToArray().Should().BeEquivalentTo(root);
            proof.LeafIndex.Should().Be(i);
            proof.TotalLeaves.Should().Be(count);

            bool isValid = MerkleTreeHelper.VerifyProof(proof);
            isValid.Should().BeTrue($"Leaf {i} proof must verify successfully against root");
        }
    }

    [Fact]
    public void VerifyProof_WithTamperedLeaf_ReturnsFalse()
    {
        var leaves = Enumerable.Range(0, 4)
            .Select(i => (ReadOnlyMemory<byte>)SHA256.HashData(Encoding.UTF8.GetBytes($"record_{i}")))
            .ToList();

        var proof = MerkleTreeHelper.GenerateProof(leaves, 2);

        // Tamper leaf hash
        byte[] tamperedLeaf = proof.LeafHash.ToArray();
        tamperedLeaf[0] ^= 0xFF;

        var tamperedProof = new MerkleInclusionProof(
            tamperedLeaf,
            proof.RootHash,
            proof.LeafIndex,
            proof.TotalLeaves,
            proof.Steps);

        bool isValid = MerkleTreeHelper.VerifyProof(tamperedProof);
        isValid.Should().BeFalse("Tampered leaf must fail verification");
    }
}
