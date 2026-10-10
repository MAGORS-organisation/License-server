using Microsoft.EntityFrameworkCore;
using Achilles.ControlPlane.Security;
using Achilles.Crypto;
using Achilles.Data;
using Achilles.Domain.Transparency;
using Achilles.Protocol;

namespace Achilles.ControlPlane.Endpoints;

public static class TransparencyEndpoints
{
    public static RouteGroupBuilder MapTransparencyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/transparency").WithTags("Transparency Log");

        group.MapGet("/root", GetTransparencyRootAsync).WithName("GetTransparencyRoot");
        group.MapGet("/sth", GetSignedTreeHeadAsync).WithName("GetSignedTreeHead");
        group.MapGet("/inclusion/{auditId}", GetInclusionProofAsync).WithName("GetInclusionProof");
        group.MapPost("/verify", VerifyInclusionProofAsync).WithName("VerifyInclusionProof");
        group.MapGet("/consistency", GetConsistencyProofAsync).WithName("GetConsistencyProof");
        group.MapPost("/verify-consistency", VerifyConsistencyProofAsync).WithName("VerifyConsistencyProof");

        return group;
    }

    private static async Task<IResult> GetTransparencyRootAsync(
        AchillesDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var entries = await db.AuditEvents
            .AsNoTracking()
            .OrderBy(e => e.TsServer)
            .ThenBy(e => e.Id)
            .Select(e => e.Hash)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var leafHashes = entries.Select(MerkleTree.HashLeaf).ToList();
        byte[] root = MerkleTree.ComputeRootHash(leafHashes);

        return TypedResults.Ok(new TransparencyRootResponseDto(
            Convert.ToHexStringLower(root),
            leafHashes.Count,
            time.GetUtcNow()));
    }

    private static async Task<IResult> GetInclusionProofAsync(
        string auditId,
        AchillesDbContext db,
        CancellationToken ct)
    {
        var entries = await db.AuditEvents
            .AsNoTracking()
            .OrderBy(e => e.TsServer)
            .ThenBy(e => e.Id)
            .Select(e => new { e.Id, e.Hash })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        int index = entries.FindIndex(e => e.Id == auditId);
        if (index < 0)
        {
            return TypedResults.NotFound($"Audit event '{auditId}' not found in the transparency log.");
        }

        var leafHashes = entries.Select(e => MerkleTree.HashLeaf(e.Hash)).ToList();
        byte[] root = MerkleTree.ComputeRootHash(leafHashes);
        var proof = MerkleTree.GenerateInclusionProof(leafHashes, index);

        byte[] targetLeafHash = leafHashes[index];
        var steps = proof.Select(p => new TransparencyProofStepDto(p.Hash, p.Direction)).ToList();

        return TypedResults.Ok(new TransparencyInclusionResponseDto(
            auditId,
            index,
            leafHashes.Count,
            Convert.ToHexStringLower(targetLeafHash),
            Convert.ToHexStringLower(root),
            steps));
    }

    private static IResult VerifyInclusionProofAsync(VerifyTransparencyProofRequestDto dto)
    {
        try
        {
            byte[] leafHash = Convert.FromHexString(dto.LeafHash);
            byte[] rootHash = Convert.FromHexString(dto.RootHash);

            var domainSteps = dto.Path
                .Select(p => new MerkleProofStep(p.Hash, p.Direction))
                .ToList();

            bool isValid = MerkleTree.VerifyInclusion(leafHash, rootHash, domainSteps);

            return TypedResults.Ok(new VerifyTransparencyProofResponseDto(
                isValid,
                isValid ? "Cryptographic inclusion proof is valid." : "Cryptographic proof verification failed. Root hash mismatch."));
        }
        catch (FormatException ex)
        {
            return TypedResults.BadRequest(new VerifyTransparencyProofResponseDto(false, $"Invalid hexadecimal encoding: {ex.Message}"));
        }
    }

    private static async Task<IResult> GetSignedTreeHeadAsync(
        AchillesDbContext db,
        KeyManager keyManager,
        TimeProvider time,
        CancellationToken ct)
    {
        var entries = await db.AuditEvents
            .AsNoTracking()
            .OrderBy(e => e.TsServer)
            .ThenBy(e => e.Id)
            .Select(e => e.Hash)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var leafHashes = entries.Select(MerkleTree.HashLeaf).ToList();
        byte[] root = MerkleTree.ComputeRootHash(leafHashes);
        string rootHex = Convert.ToHexStringLower(root);
        var now = time.GetUtcNow();

        byte[] signingPayload = MerkleTree.ComputeTreeHeadSigningPayload(leafHashes.Count, now, rootHex);
        var key = await keyManager.GetActiveSigningKeyAsync(ct).ConfigureAwait(false);
        byte[] sig = new byte[key.SignatureSize];
        key.Sign(signingPayload, sig);

        return TypedResults.Ok(new SignedTreeHeadDto(
            leafHashes.Count,
            now,
            rootHex,
            Convert.ToBase64String(sig),
            key.Kid,
            key.Alg));
    }

    private static async Task<IResult> GetConsistencyProofAsync(
        int oldSize,
        int newSize,
        AchillesDbContext db,
        CancellationToken ct)
    {
        if (oldSize <= 0 || newSize < oldSize)
        {
            return TypedResults.BadRequest("oldSize must be greater than zero and less than or equal to newSize.");
        }

        var entries = await db.AuditEvents
            .AsNoTracking()
            .OrderBy(e => e.TsServer)
            .ThenBy(e => e.Id)
            .Take(newSize)
            .Select(e => e.Hash)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (entries.Count < newSize)
        {
            return TypedResults.BadRequest($"Log contains only {entries.Count} entries, which is fewer than requested newSize {newSize}.");
        }

        var leafHashes = entries.Select(MerkleTree.HashLeaf).ToList();
        byte[] oldRoot = MerkleTree.ComputeRootHash(leafHashes.Take(oldSize).ToList());
        byte[] newRoot = MerkleTree.ComputeRootHash(leafHashes);
        var proof = MerkleTree.GenerateConsistencyProof(leafHashes, oldSize, newSize);

        return TypedResults.Ok(new TransparencyConsistencyResponseDto(
            oldSize,
            newSize,
            Convert.ToHexStringLower(oldRoot),
            Convert.ToHexStringLower(newRoot),
            proof));
    }

    private static IResult VerifyConsistencyProofAsync(VerifyTransparencyConsistencyRequestDto dto)
    {
        try
        {
            byte[] oldRoot = Convert.FromHexString(dto.OldRoot);
            byte[] newRoot = Convert.FromHexString(dto.NewRoot);

            bool isConsistent = MerkleTree.VerifyConsistency(oldRoot, newRoot, dto.OldSize, dto.NewSize, dto.Proof);

            return TypedResults.Ok(new VerifyTransparencyConsistencyResponseDto(
                isConsistent,
                isConsistent
                    ? "Cryptographic consistency proof is valid. Append-only ledger integrity confirmed."
                    : "Cryptographic consistency proof verification failed. Tree history mismatch."));
        }
        catch (FormatException ex)
        {
            return TypedResults.BadRequest(new VerifyTransparencyConsistencyResponseDto(false, $"Invalid hexadecimal encoding: {ex.Message}"));
        }
    }
}

