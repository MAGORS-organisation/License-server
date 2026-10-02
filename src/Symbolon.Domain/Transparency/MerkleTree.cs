using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Symbolon.Domain.Transparency;

public sealed record MerkleProofStep(string Hash, string Direction); // "left" or "right"

public sealed record MerkleInclusionProof(
    string AuditId,
    int LeafIndex,
    int TreeSize,
    string LeafHash,
    string RootHash,
    IReadOnlyList<MerkleProofStep> Path);

public sealed record SignedTreeHead(
    int TreeSize,
    DateTimeOffset Timestamp,
    string RootHash,
    string Signature,
    string KeyId,
    string Algorithm);

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
        // so to verify from leaf to root, we traverse the path in reverse order.
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

    /// <summary>
    /// Generates a consistency proof between an older tree of size m and a newer tree of size n (RFC 6962 §2.1.2).
    /// </summary>
    public static IReadOnlyList<string> GenerateConsistencyProof(IReadOnlyList<byte[]> leafHashes, int m, int n)
    {
        ArgumentNullException.ThrowIfNull(leafHashes);

        if (m <= 0 || m > n || n > leafHashes.Count)
        {
            throw new ArgumentOutOfRangeException(nameof(m), "Invalid tree sizes for consistency proof.");
        }

        if (m == n)
        {
            return [];
        }

        var proof = new List<string>();
        BuildSubproof(leafHashes, 0, m, n, true, proof);
        return proof;
    }

    private static void BuildSubproof(IReadOnlyList<byte[]> leaves, int offset, int m, int n, bool b, List<string> proof)
    {
        if (m == n)
        {
            if (!b)
            {
                byte[] hash = ComputeSubtreeMth(leaves, offset, m);
                proof.Add(Convert.ToHexStringLower(hash));
            }
            return;
        }

        int k = LargestPowerOfTwoLessThan(n);

        if (m <= k)
        {
            BuildSubproof(leaves, offset, m, k, b, proof);
            byte[] rightHash = ComputeSubtreeMth(leaves, offset + k, n - k);
            proof.Add(Convert.ToHexStringLower(rightHash));
        }
        else
        {
            BuildSubproof(leaves, offset + k, m - k, n - k, false, proof);
            byte[] leftHash = ComputeSubtreeMth(leaves, offset, k);
            proof.Add(Convert.ToHexStringLower(leftHash));
        }
    }

    /// <summary>
    /// Verifies a consistency proof between an older root hash at size m and a newer root hash at size n (RFC 6962 §2.1.2 / RFC 9162 §2.1.4.2).
    /// </summary>
    public static bool VerifyConsistency(byte[] oldRoot, byte[] newRoot, int m, int n, IReadOnlyList<string> proof)
    {
        ArgumentNullException.ThrowIfNull(oldRoot);
        ArgumentNullException.ThrowIfNull(newRoot);
        ArgumentNullException.ThrowIfNull(proof);

        if (m <= 0 || m > n) return false;
        if (m == n)
        {
            return proof.Count == 0 && CryptographicOperations.FixedTimeEquals(oldRoot, newRoot);
        }

        if (proof.Count == 0) return false;

        var p = proof.Select(Convert.FromHexString).ToList();

        // RFC 9162 §2.1.4.2 step 2: If first is an exact power of 2, prepend first_hash
        if (BitOperations.IsPow2(m))
        {
            p.Insert(0, oldRoot);
        }

        // Step 3: fn = first - 1, sn = second - 1
        int fn = m - 1;
        int sn = n - 1;

        // Step 4: If LSB(fn) is set, right-shift fn and sn equally until LSB(fn) is not set
        while ((fn & 1) == 1)
        {
            fn >>= 1;
            sn >>= 1;
        }

        // Step 5: Set fr and sr to first value in path
        if (p.Count == 0) return false;
        byte[] fr = p[0];
        byte[] sr = p[0];

        // Step 6: For each subsequent value c in consistency_path
        for (int i = 1; i < p.Count; i++)
        {
            byte[] c = p[i];

            // 6a: If sn is 0, fail
            if (sn == 0) return false;

            // 6b: If LSB(fn) is set, or if fn == sn
            if ((fn & 1) == 1 || fn == sn)
            {
                fr = HashNode(c, fr);
                sr = HashNode(c, sr);

                if ((fn & 1) == 0)
                {
                    while ((fn & 1) == 0 && fn != 0)
                    {
                        fn >>= 1;
                        sn >>= 1;
                    }
                }
            }
            else
            {
                sr = HashNode(sr, c);
            }

            // 6c: Finally, right-shift both fn and sn one time
            fn >>= 1;
            sn >>= 1;
        }

        // Step 7: Verify fr == oldRoot, sr == newRoot, sn == 0
        return sn == 0 &&
               CryptographicOperations.FixedTimeEquals(fr, oldRoot) &&
               CryptographicOperations.FixedTimeEquals(sr, newRoot);
    }

    /// <summary>
    /// Computes the canonical payload for signing a Signed Tree Head (STH).
    /// </summary>
    public static byte[] ComputeTreeHeadSigningPayload(int treeSize, DateTimeOffset timestamp, string rootHash)
    {
        ArgumentNullException.ThrowIfNull(rootHash);
        string header = $"SYMBOLON-STH:v1:{treeSize}:{timestamp.ToUnixTimeMilliseconds()}:{rootHash.ToUpperInvariant()}";
        return Encoding.UTF8.GetBytes(header);
    }

    private static int LargestPowerOfTwoLessThan(int n)
    {
        if (n <= 1) return 0;
        int p = (int)BitOperations.RoundUpToPowerOf2((uint)n);
        return p == n ? n / 2 : p / 2;
    }
}
