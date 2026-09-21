using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Symbolon.ControlPlane.Models;
using Symbolon.Data;
using Symbolon.Data.Entities;

namespace Symbolon.ControlPlane.Endpoints;

public static class ApiKeyEndpoints
{
    public static RouteGroupBuilder MapApiKeyEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/v1/api-keys").WithTags("API Keys");

        group.MapPost("", CreateApiKeyAsync).WithName("CreateApiKey");
        group.MapGet("", GetApiKeysAsync).WithName("GetApiKeys");
        group.MapDelete("/{id}", RevokeApiKeyAsync).WithName("RevokeApiKey");

        return group;
    }

    private static async Task<IResult> CreateApiKeyAsync(
        CreateApiKeyDto dto,
        HttpContext httpContext,
        SymbolonDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        string? tenantId = httpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault()
            ?? httpContext.User.FindFirst("tenant_id")?.Value;

        if (string.IsNullOrWhiteSpace(tenantId))
        {
            var firstTenant = await db.Tenants.FirstOrDefaultAsync(ct).ConfigureAwait(false);
            tenantId = firstTenant?.Id ?? "ten_default";
        }

        string prefix = $"sym_adm_{Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(4))}";
        string secret = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        string fullKey = $"{prefix}_{secret}";

        byte[] keyHash = SHA256.HashData(Encoding.UTF8.GetBytes(fullKey));
        string keyHashHex = Convert.ToHexStringLower(keyHash);

        var entity = new ApiKeyEntity
        {
            Id = $"key_{Guid.NewGuid():N}",
            TenantId = tenantId,
            Name = dto.Name.Trim(),
            Prefix = prefix,
            KeyHash = keyHashHex,
            Role = string.IsNullOrWhiteSpace(dto.Role) ? "admin:super" : dto.Role.Trim(),
            ExpiresAt = dto.ExpiresAt,
            CreatedAt = time.GetUtcNow()
        };

        db.ApiKeys.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return TypedResults.Created($"/admin/v1/api-keys/{entity.Id}", new ApiKeyResponseDto(
            entity.Id,
            entity.TenantId,
            entity.Name,
            entity.Prefix,
            entity.Role,
            entity.ExpiresAt,
            entity.CreatedAt,
            SecretKey: fullKey));
    }

    private static async Task<IResult> GetApiKeysAsync(
        HttpContext httpContext,
        SymbolonDbContext db,
        CancellationToken ct)
    {
        string? tenantId = httpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault()
            ?? httpContext.User.FindFirst("tenant_id")?.Value;

        var query = db.ApiKeys.AsNoTracking().Where(k => k.RevokedAt == null);

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            query = query.Where(k => k.TenantId == tenantId);
        }

        var keys = await query
            .OrderByDescending(k => k.CreatedAt)
            .Select(k => new ApiKeyResponseDto(
                k.Id,
                k.TenantId,
                k.Name,
                k.Prefix,
                k.Role,
                k.ExpiresAt,
                k.CreatedAt,
                null))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return TypedResults.Ok(keys);
    }

    private static async Task<IResult> RevokeApiKeyAsync(
        string id,
        SymbolonDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var key = await db.ApiKeys.FirstOrDefaultAsync(k => k.Id == id, ct).ConfigureAwait(false);
        if (key is null || key.RevokedAt is not null)
        {
            return TypedResults.NotFound();
        }

        key.RevokedAt = time.GetUtcNow();
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return TypedResults.Ok(new { message = $"API key '{id}' successfully revoked.", id });
    }
}
