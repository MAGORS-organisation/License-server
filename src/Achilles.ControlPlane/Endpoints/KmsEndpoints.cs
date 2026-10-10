using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Achilles.ControlPlane.Security;
using Achilles.Crypto.Hierarchy;
using Achilles.Crypto.Kms;

namespace Achilles.ControlPlane.Endpoints;

public static class KmsEndpoints
{
    public static void MapKmsEndpoints(this IEndpointRouteBuilder app)
    {
        var kmsGroup = app.MapGroup("/admin/v1/kms")
            .WithTags("KMS & Hardware Isolation");

        kmsGroup.MapGet("/status", async (KmsHierarchyManager manager, CancellationToken ct) =>
        {
            var health = await manager.KmsProvider.CheckHealthAsync(ct).ConfigureAwait(false);
            var keys = await manager.KmsProvider.ListKeysAsync(ct).ConfigureAwait(false);

            return Results.Ok(new
            {
                isHealthy = health.IsHealthy,
                providerType = health.ProviderType.ToString(),
                details = health.Details,
                keysCount = keys.Count,
                keys,
                timestamp = health.Timestamp
            });
        });

        kmsGroup.MapGet("/hsm-status", async (KmsHierarchyManager manager, CancellationToken ct) =>
        {
            var health = await manager.KmsProvider.CheckHealthAsync(ct).ConfigureAwait(false);
            var keys = await manager.KmsProvider.ListKeysAsync(ct).ConfigureAwait(false);

            return Results.Ok(new
            {
                isHealthy = health.IsHealthy,
                providerType = health.ProviderType.ToString(),
                details = health.Details,
                keysCount = keys.Count,
                keys,
                timestamp = health.Timestamp
            });
        });

        var keysAdminGroup = app.MapGroup("/admin/v1/keys")
            .WithTags("KMS & Hardware Isolation");

        keysAdminGroup.MapGet("/hsm-status", async (KmsHierarchyManager manager, CancellationToken ct) =>
        {
            var health = await manager.KmsProvider.CheckHealthAsync(ct).ConfigureAwait(false);
            var keys = await manager.KmsProvider.ListKeysAsync(ct).ConfigureAwait(false);

            return Results.Ok(new
            {
                isHealthy = health.IsHealthy,
                providerType = health.ProviderType.ToString(),
                details = health.Details,
                keysCount = keys.Count,
                keys,
                timestamp = health.Timestamp
            });
        });

        var hierarchyGroup = app.MapGroup("/admin/v1/keys/hierarchy")
            .WithTags("Key Hierarchy");

        hierarchyGroup.MapGet("/", (KmsHierarchyManager manager) =>
        {
            return Results.Ok(manager.CurrentChain);
        });

        hierarchyGroup.MapPost("/verify", (KeyHierarchyChain? chain, KmsHierarchyManager manager) =>
        {
            var targetChain = chain ?? manager.CurrentChain;
            var result = KeyHierarchyEngine.VerifyChain(targetChain, manager.RootAnchor);
            return Results.Ok(result);
        });
    }
}
