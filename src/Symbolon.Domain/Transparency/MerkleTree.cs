using System.Numerics;
using System.Security.Cryptography;

namespace Symbolon.Domain.Transparency;

public sealed record MerkleProofStep(string Hash, string Direction); // "left" or "right"

public sealed record MerkleInclusionProof(
    string AuditId,
    int LeafIndex,
    int TreeSize,
    string LeafHash,
    string RootHash,
    IReadOnlyList<MerkleProofStep> Path);

public static class MerkleTree
{
    public static byte[] HashLeaf(byte[] entryData)
    {
        ArgumentNullException.ThrowIfNull(entryData);
        byte[] buffer = new byte[1 + entryData.Length];
        buffer[0] = 0x00; // RFC 6962 leaf domain separator
        entryData.CopyTo(buffer, 1);
        return SHA256.HashData(buffer);
    }

    public static byte[] HashNode(byte[] left, byte[] right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);
        byte[] buffer = new byte[1 + left.Length + right.Length];
        buffer[0] = 0x01; // RFC 6962 internal node domain separator
        left.CopyTo(buffer, 1);
        right.CopyTo(buffer, 1 + left.Length);
        return SHA256.HashData(buffer);
    }

    /// <summary>
    /// Calculates the Merkle Tree Hash (MTH) according to RFC 6962 §2.1.
    /// </summary>
    public static byte[] ComputeRootHash(IReadOnlyList<byte[]> leafHashes)
    {
        ArgumentNullException.ThrowIfNull(leafHashes);

        if (leafHashes.Count == 0)
        {
            return SHA256.HashData([]);
        }

        return ComputeSubtreeMth(leafHashes, 0, leafHashes.Count);
    }

    private static byte[] ComputeSubtreeMth(IReadOnlyList<byte[]> leaves, int offset, int length)
    {
        if (length == 0)
        {
            return SHA256.HashData([]);
        }

        if (length == 1)
        {
            return leaves[offset];
        }

        int k = LargestPowerOfTwoLessThan(length);
        byte[] left = ComputeSubtreeMth(leaves, offset, k);
        byte[] right = ComputeSubtreeMth(leaves, offset + k, length - k);
        return HashNode(left, right);
    }

    /// <summary>
    /// Generates an inclusion proof (audit path) for the leaf at index m in a tree of size n (RFC 6962 §2.1.1).
    /// </summary>
    public static IReadOnlyList<MerkleProofStep> GenerateInclusionProof(IReadOnlyList<byte[]> leafHashes, int m)
    {
        ArgumentNullException.ThrowIfNull(leafHashes);

        if (m < 0 || m >= leafHashes.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(m), "Leaf index is out of range.");
        }

        var path = new List<MerkleProofStep>();
        BuildSubtreeAuditPath(leafHashes, 0, leafHashes.Count, m, path);
        return path;
    }

    private static void BuildSubtreeAuditPath(IReadOnlyList<byte[]> leaves, int offset, int length, int m, List<MerkleProofStep> path)
    {
        if (length <= 1)
        {
            return;
        }

        int k = LargestPowerOfTwoLessThan(length);

        if (m < k)
        {
            // Sibling subtree is on the right
            byte[] rightSubtreeHash = ComputeSubtreeMth(leaves, offset + k, length - k);
            path.Add(new MerkleProofStep(Convert.ToHexStringLower(rightSubtreeHash), "right"));
            BuildSubtreeAuditPath(leaves, offset, k, m, path);
        }
        else
        {
            // Sibling subtree is on the left
            byte[] leftSubtreeHash = ComputeSubtreeMth(leaves, offset, k);
            path.Add(new MerkleProofStep(Convert.ToHexStringLower(leftSubtreeHash), "left"));
            BuildSubtreeAuditPath(leaves, offset + k, length - k, m - k, path);
        }
    }

    /// <summary>
    /// Verifies that the leaf is included in the tree with the specified root hash using the audit path.
    /// </summary>
    public static bool VerifyInclusion(byte[] leafHash, byte[] expectedRoot, IReadOnlyList<MerkleProofStep> path)
    {
        ArgumentNullException.ThrowIfNull(leafHash);
        ArgumentNullException.ThrowIfNull(expectedRoot);
        ArgumentNullException.ThrowIfNull(path);

        byte[] current = leafHash;

        // The path was added top-down (root level first down to leaf level),
        // so to verify from leaf to root, we traverse the path in reverse order!
        for (int i = path.Count - 1; i >= 0; i--)
        {
            var step = path[i];
            byte[] sibling = Convert.FromHexString(step.Hash);

            current = step.Direction switch
            {
                "right" => HashNode(current, sibling),
                "left" => HashNode(sibling, current),
                _ => throw new InvalidOperationException($"Invalid proof step direction '{step.Direction}'.")
            };
        }

        return CryptographicOperations.FixedTimeEquals(current, expectedRoot);
    }

    private static int LargestPowerOfTwoLessThan(int n)
    {
        if (n <= 1) return 0;
        int p = (int)BitOperations.RoundUpToPowerOf2((uint)n);
        return p == n ? n / 2 : p / 2;
    }
}
