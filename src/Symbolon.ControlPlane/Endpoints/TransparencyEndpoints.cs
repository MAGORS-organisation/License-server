using Microsoft.EntityFrameworkCore;
using Symbolon.Data;
using Symbolon.Domain.Transparency;
using Symbolon.Protocol;

namespace Symbolon.ControlPlane.Endpoints;

public static class TransparencyEndpoints
{
    public static RouteGroupBuilder MapTransparencyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/transparency").WithTags("Transparency Log");

        group.MapGet("/root", GetTransparencyRootAsync).WithName("GetTransparencyRoot");
        group.MapGet("/inclusion/{auditId}", GetInclusionProofAsync).WithName("GetInclusionProof");
        group.MapPost("/verify", VerifyInclusionProofAsync).WithName("VerifyInclusionProof");

        return group;
    }

    private static async Task<IResult> GetTransparencyRootAsync(
        SymbolonDbContext db,
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
        SymbolonDbContext db,
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
}
