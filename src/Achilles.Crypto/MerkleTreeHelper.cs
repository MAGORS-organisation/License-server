using System.Security.Cryptography;

namespace Achilles.Crypto;

/// <summary>
/// A single step in a Merkle inclusion proof path.
/// </summary>
public sealed record MerkleInclusionStep(ReadOnlyMemory<byte> SiblingHash, bool IsLeft);

/// <summary>
/// Cryptographic Merkle inclusion proof allowing third-party verification
/// that a specific leaf was included in the notarized Merkle Root.
/// </summary>
public sealed record MerkleInclusionProof(
    ReadOnlyMemory<byte> LeafHash,
    ReadOnlyMemory<byte> RootHash,
    int LeafIndex,
    int TotalLeaves,
    IReadOnlyList<MerkleInclusionStep> Steps);

/// <summary>
/// Cryptographic Merkle Tree builder and inclusion proof verifier (§10.8).
/// Uses RFC 6962 / Bitcoin style binary Merkle trees with SHA-256.
/// </summary>
public static class MerkleTreeHelper
{
    /// <summary>
    /// Computes the 32-byte Merkle Root for a list of leaf hashes.
    /// If leaves is empty, returns 32 zero bytes.
    /// </summary>
    public static byte[] ComputeRoot(IReadOnlyList<ReadOnlyMemory<byte>> leafHashes)
    {
        ArgumentNullException.ThrowIfNull(leafHashes);

        if (leafHashes.Count == 0)
        {
            return new byte[32];
        }

        if (leafHashes.Count == 1)
        {
            return leafHashes[0].ToArray();
        }

        var currentLevel = new List<byte[]>(leafHashes.Count);
        foreach (var l in leafHashes)
        {
            currentLevel.Add(l.ToArray());
        }

        while (currentLevel.Count > 1)
        {
            var nextLevel = new List<byte[]>((currentLevel.Count + 1) / 2);

            for (int i = 0; i < currentLevel.Count; i += 2)
            {
                byte[] left = currentLevel[i];
                byte[] right = (i + 1 < currentLevel.Count) ? currentLevel[i + 1] : left;

                byte[] combined = new byte[left.Length + right.Length];
                Buffer.BlockCopy(left, 0, combined, 0, left.Length);
                Buffer.BlockCopy(right, 0, combined, left.Length, right.Length);

                nextLevel.Add(SHA256.HashData(combined));
            }

            currentLevel = nextLevel;
        }

        return currentLevel[0];
    }

    /// <summary>
    /// Generates a Merkle inclusion proof for a leaf at the specified index.
    /// </summary>
    public static MerkleInclusionProof GenerateProof(IReadOnlyList<ReadOnlyMemory<byte>> leafHashes, int leafIndex)
    {
        ArgumentNullException.ThrowIfNull(leafHashes);

        if (leafHashes.Count == 0)
        {
            throw new ArgumentException("Leaf list cannot be empty.", nameof(leafHashes));
        }

        if (leafIndex < 0 || leafIndex >= leafHashes.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(leafIndex), "Leaf index out of bounds.");
        }

        byte[] targetLeaf = leafHashes[leafIndex].ToArray();
        byte[] root = ComputeRoot(leafHashes);

        if (leafHashes.Count == 1)
        {
            return new MerkleInclusionProof(targetLeaf, root, leafIndex, 1, []);
        }

        var steps = new List<MerkleInclusionStep>();
        var currentLevel = new List<byte[]>(leafHashes.Count);
        foreach (var l in leafHashes)
        {
            currentLevel.Add(l.ToArray());
        }

        int currentIndex = leafIndex;

        while (currentLevel.Count > 1)
        {
            var nextLevel = new List<byte[]>((currentLevel.Count + 1) / 2);
            bool isCurrentOdd = (currentIndex % 2 == 1);
            int siblingIndex = isCurrentOdd ? currentIndex - 1 : currentIndex + 1;

            if (siblingIndex >= currentLevel.Count)
            {
                siblingIndex = currentIndex;
            }

            byte[] sibling = (byte[])currentLevel[siblingIndex].Clone();
            steps.Add(new MerkleInclusionStep(sibling, IsLeft: isCurrentOdd));

            for (int i = 0; i < currentLevel.Count; i += 2)
            {
                byte[] left = currentLevel[i];
                byte[] right = (i + 1 < currentLevel.Count) ? currentLevel[i + 1] : left;

                byte[] combined = new byte[left.Length + right.Length];
                Buffer.BlockCopy(left, 0, combined, 0, left.Length);
                Buffer.BlockCopy(right, 0, combined, left.Length, right.Length);

                nextLevel.Add(SHA256.HashData(combined));
            }

            currentIndex /= 2;
            currentLevel = nextLevel;
        }

        return new MerkleInclusionProof(targetLeaf, root, leafIndex, leafHashes.Count, steps);
    }

    /// <summary>
    /// Cryptographically verifies a Merkle inclusion proof against the expected root.
    /// </summary>
    public static bool VerifyProof(MerkleInclusionProof proof)
    {
        ArgumentNullException.ThrowIfNull(proof);

        if (proof.Steps.Count == 0)
        {
            return CryptographicOperations.FixedTimeEquals(proof.LeafHash.Span, proof.RootHash.Span);
        }

        byte[] currentHash = proof.LeafHash.ToArray();

        foreach (var step in proof.Steps)
        {
            byte[] sibling = step.SiblingHash.ToArray();
            byte[] left = step.IsLeft ? sibling : currentHash;
            byte[] right = step.IsLeft ? currentHash : sibling;

            byte[] combined = new byte[left.Length + right.Length];
            Buffer.BlockCopy(left, 0, combined, 0, left.Length);
            Buffer.BlockCopy(right, 0, combined, left.Length, right.Length);

            currentHash = SHA256.HashData(combined);
        }

        return CryptographicOperations.FixedTimeEquals(currentHash, proof.RootHash.Span);
    }
}
