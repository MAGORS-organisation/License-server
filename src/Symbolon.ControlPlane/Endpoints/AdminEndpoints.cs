using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Symbolon.ControlPlane.Models;
using Symbolon.Data;
using Symbolon.Data.Entities;
using Symbolon.Domain;
using Symbolon.Format;

namespace Symbolon.ControlPlane.Endpoints;

public static class AdminEndpoints
{
    public static RouteGroupBuilder MapAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/v1").WithTags("Admin");

        // Tenants
        group.MapPost("/tenants", CreateTenantAsync).WithName("CreateTenant");
        group.MapGet("/tenants", GetTenantsAsync).WithName("GetTenants");

        // Products
        group.MapPost("/products", CreateProductAsync).WithName("CreateProduct");
        group.MapGet("/products", GetProductsAsync).WithName("GetProducts");

        // Policies
        group.MapPost("/policies", CreatePolicyAsync).WithName("CreatePolicy");
        group.MapGet("/policies", GetPoliciesAsync).WithName("GetPolicies");

        // Licenses
        group.MapPost("/licenses", IssueLicenseAsync).WithName("IssueLicense");
        group.MapGet("/licenses", GetLicensesAsync).WithName("GetLicenses");
        group.MapGet("/licenses/{id}", GetLicenseByIdAsync).WithName("GetLicenseById");
        group.MapPost("/licenses/{id}/revoke", RevokeLicenseAsync).WithName("RevokeLicense");

        // Audit & Reports
        group.MapGet("/audit", GetAuditEventsAsync).WithName("GetAuditEvents");
        group.MapGet("/reports/concurrency", GetConcurrencyReportAsync).WithName("GetConcurrencyReport");

        // Key Management & Rotation
        group.MapGet("/keys", GetKeysAsync).WithName("GetKeys");
        group.MapPost("/keys/rotate", RotateKeyAsync).WithName("RotateKey");
        group.MapPost("/keys/{kid}/revoke", RevokeKeyAsync).WithName("RevokeKey");

        // Named Users & Options
        group.MapPost("/licenses/{id}/users", AssignLicenseUserAsync).WithName("AssignLicenseUser");
        group.MapGet("/licenses/{id}/users", GetLicenseUsersAsync).WithName("GetLicenseUsers");
        group.MapDelete("/licenses/{id}/users/{userId}", RemoveLicenseUserAsync).WithName("RemoveLicenseUser");

        // Quotas & Metered Units
        group.MapPost("/licenses/{id}/quotas", SetLicenseQuotaAsync).WithName("SetLicenseQuota");
        group.MapGet("/licenses/{id}/quotas", GetLicenseQuotasAsync).WithName("GetLicenseQuotas");

        return group;
    }

    private static async Task<IResult> CreateTenantAsync(CreateTenantDto dto, SymbolonDbContext db, TimeProvider time, CancellationToken ct)
    {
        string id = $"ten_{Guid.NewGuid():N}";
        var tenant = new Tenant
        {
            Id = id,
            Slug = dto.Slug.ToLowerInvariant().Trim(),
            Name = dto.Name.Trim(),
            CreatedAt = time.GetUtcNow()
        };

        db.Tenants.Add(tenant);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return TypedResults.Created($"/admin/v1/tenants/{id}", new TenantDto(tenant.Id, tenant.Slug, tenant.Name, tenant.CreatedAt));
    }

    private static async Task<IResult> GetTenantsAsync(SymbolonDbContext db, CancellationToken ct)
    {
        var list = await db.Tenants
            .Select(t => new TenantDto(t.Id, t.Slug, t.Name, t.CreatedAt))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return TypedResults.Ok(list);
    }

    private static async Task<IResult> CreateProductAsync(
        CreateProductDto dto,
        HttpContext context,
        SymbolonDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        string tenantId = context.Request.Headers["X-Tenant-Id"].FirstOrDefault()
            ?? (await db.Tenants.Select(t => t.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false))
            ?? "default";

        string id = $"prd_{Guid.NewGuid():N}";
        var product = new Product
        {
            Id = id,
            TenantId = tenantId,
            Code = dto.Code.Trim(),
            Name = dto.Name.Trim(),
            PlatformsJson = JsonSerializer.Serialize(dto.Platforms ?? ["windows", "linux", "macos"]),
            CreatedAt = time.GetUtcNow()
        };

        db.Products.Add(product);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return TypedResults.Created($"/admin/v1/products/{id}", new ProductDto(product.Id, product.TenantId, product.Code, product.Name, dto.Platforms ?? [], product.CreatedAt));
    }

    private static async Task<IResult> GetProductsAsync(SymbolonDbContext db, CancellationToken ct)
    {
        var list = await db.Products
            .Select(p => new ProductDto(p.Id, p.TenantId, p.Code, p.Name, new List<string>(), p.CreatedAt))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return TypedResults.Ok(list);
    }

    private static async Task<IResult> CreatePolicyAsync(
        CreatePolicyDto dto,
        HttpContext context,
        SymbolonDbContext db,
        CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == dto.ProductId, ct).ConfigureAwait(false);
        if (product is null)
        {
            return TypedResults.NotFound($"Product '{dto.ProductId}' not found.");
        }

        string id = $"pol_{Guid.NewGuid():N}";
        var policy = new Policy
        {
            Id = id,
            TenantId = product.TenantId,
            ProductId = product.Id,
            Code = dto.Code.Trim(),
            Name = dto.Name.Trim(),
            LicenseModel = dto.LicenseModel,
            Duration = dto.Duration,
            MaxSeats = dto.MaxSeats,
            SeatUnit = dto.SeatUnit,
            OverageStrategy = dto.OverageStrategy,
            ExpirationStrategy = dto.ExpirationStrategy,
            LeaseTtlSeconds = dto.LeaseTtlSeconds,
            HeartbeatIntervalSeconds = dto.HeartbeatIntervalSeconds,
            GraceTtlSeconds = dto.GraceTtlSeconds,
            ResurrectionWindowSeconds = dto.ResurrectionWindowSeconds,
            BorrowEnabled = dto.BorrowEnabled,
            BorrowMaxDurationDays = dto.BorrowMaxDurationDays,
            OfflineAllowed = dto.OfflineAllowed,
            CryptoProfile = dto.CryptoProfile,
            EntitlementsJson = JsonSerializer.Serialize(dto.Entitlements ?? ["core"])
        };

        db.Policies.Add(policy);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return TypedResults.Created($"/admin/v1/policies/{id}", new PolicyDto(policy.Id, policy.TenantId, policy.ProductId, policy.Code, policy.Name, policy.LicenseModel, policy.MaxSeats, policy.SeatUnit, policy.LeaseTtlSeconds));
    }

    private static async Task<IResult> GetPoliciesAsync(SymbolonDbContext db, CancellationToken ct)
    {
        var list = await db.Policies
            .Select(p => new PolicyDto(p.Id, p.TenantId, p.ProductId, p.Code, p.Name, p.LicenseModel, p.MaxSeats, p.SeatUnit, p.LeaseTtlSeconds))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return TypedResults.Ok(list);
    }

    private static async Task<IResult> IssueLicenseAsync(
        CreateLicenseDto dto,
        SymbolonDbContext db,
        IAuditLedger audit,
        Webhooks.IWebhookDispatcher webhooks,
        TimeProvider time,
        CancellationToken ct)
    {
        var policy = await db.Policies.FirstOrDefaultAsync(p => p.Id == dto.PolicyId, ct).ConfigureAwait(false);
        if (policy is null)
        {
            return TypedResults.NotFound($"Policy '{dto.PolicyId}' not found.");
        }

        string rawKey = LicenseKey.Generate(dto.KeyPrefix ?? "SYM").Canonical;
        byte[] lookup = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey))[..4];
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey)));

        int seats = dto.MaxSeats ?? policy.MaxSeats;
        if (seats <= 0) seats = 1;

        string licenseId = $"lic_{Guid.NewGuid():N}";
        var now = time.GetUtcNow();
        var expiresAt = dto.ExpiresAt ?? now.AddDays(365);

        var license = new LicenseEntity
        {
            Id = licenseId,
            TenantId = policy.TenantId,
            PolicyId = policy.Id,
            KeyLookup = lookup,
            KeyHash = hash,
            CustomerRef = dto.CustomerRef,
            State = "active",
            MaxSeats = seats,
            IssuedAt = now,
            ExpiresAt = expiresAt,
            CreatedAt = now
        };

        db.Licenses.Add(license);

        // Materialize seats in DB (Invariant: exactly N rows created)
        for (int i = 1; i <= seats; i++)
        {
            db.Seats.Add(new SeatEntity
            {
                LicenseId = licenseId,
                SeatNo = i,
                IsOverage = false
            });
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await audit.AppendAsync(new AuditEvent("license.issued", licenseId, null, null, now, $"Issued license with {seats} seats"), ct).ConfigureAwait(false);

        await webhooks.PublishEventAsync("license.created", new
        {
            licenseId = license.Id,
            policyId = license.PolicyId,
            tenantId = license.TenantId,
            customerRef = license.CustomerRef,
            maxSeats = license.MaxSeats,
            expiresAt = license.ExpiresAt
        }, license.TenantId, ct).ConfigureAwait(false);

        return TypedResults.Created($"/admin/v1/licenses/{licenseId}", new LicenseResponseDto(
            license.Id,
            rawKey,
            license.TenantId,
            license.PolicyId,
            license.CustomerRef,
            license.State,
            license.MaxSeats,
            license.IssuedAt,
            license.ExpiresAt));
    }

    private static async Task<IResult> GetLicensesAsync(SymbolonDbContext db, CancellationToken ct)
    {
        var list = await db.Licenses
            .Select(l => new LicenseResponseDto(
                l.Id,
                null,
                l.TenantId,
                l.PolicyId,
                l.CustomerRef,
                l.State,
                l.MaxSeats,
                l.IssuedAt,
                l.ExpiresAt))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return TypedResults.Ok(list);
    }

    private static async Task<IResult> GetLicenseByIdAsync(string id, SymbolonDbContext db, CancellationToken ct)
    {
        var l = await db.Licenses.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (l is null) return TypedResults.NotFound();

        return TypedResults.Ok(new LicenseResponseDto(
            l.Id,
            null,
            l.TenantId,
            l.PolicyId,
            l.CustomerRef,
            l.State,
            l.MaxSeats,
            l.IssuedAt,
            l.ExpiresAt));
    }

    private static async Task<IResult> RevokeLicenseAsync(
        string id,
        RevokeLicenseDto dto,
        SymbolonDbContext db,
        IAuditLedger audit,
        Webhooks.IWebhookDispatcher webhooks,
        TimeProvider time,
        CancellationToken ct)
    {
        var license = await db.Licenses.FirstOrDefaultAsync(l => l.Id == id, ct).ConfigureAwait(false);
        if (license is null) return TypedResults.NotFound();

        license.State = "revoked";

        var now = time.GetUtcNow();
        var rev = new RevocationEntity
        {
            Id = $"rev_{Guid.NewGuid():N}",
            TenantId = license.TenantId,
            SubjectType = "license",
            SubjectId = license.Id,
            Reason = dto.Reason,
            RevokedAt = now
        };
        db.Revocations.Add(rev);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await audit.AppendAsync(new AuditEvent("license.revoked", license.Id, null, null, now, dto.Reason), ct).ConfigureAwait(false);

        await webhooks.PublishEventAsync("license.revoked", new
        {
            licenseId = license.Id,
            tenantId = license.TenantId,
            reason = dto.Reason,
            revokedAt = now
        }, license.TenantId, ct).ConfigureAwait(false);

        return TypedResults.Ok(new { message = $"License {id} revoked.", reason = dto.Reason });
    }

    private static async Task<IResult> GetAuditEventsAsync(
        string? licenseId,
        int? limit,
        SymbolonDbContext db,
        CancellationToken ct)
    {
        int max = limit ?? 50;
        var query = db.AuditEvents.AsQueryable();

        if (!string.IsNullOrWhiteSpace(licenseId))
        {
            query = query.Where(a => a.LicenseId == licenseId);
        }

        var events = await query
            .OrderByDescending(a => a.TsServer)
            .Take(max)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return TypedResults.Ok(events);
    }

    private static async Task<IResult> GetConcurrencyReportAsync(
        SymbolonDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var now = time.GetUtcNow();
        int totalSeats = await db.Seats.CountAsync(ct).ConfigureAwait(false);
        int activeLeases = await db.Seats.CountAsync(s => s.LeaseId != null && s.ExpiresAt > now, ct).ConfigureAwait(false);
        int availableSeats = totalSeats - activeLeases;
        double util = totalSeats > 0 ? (double)activeLeases / totalSeats * 100.0 : 0.0;

        var yesterday = now.AddHours(-24);
        int denials = await db.AuditEvents.CountAsync(a => a.Type == "deny" && a.TsServer > yesterday, ct).ConfigureAwait(false);

        return TypedResults.Ok(new ConcurrencyReportDto(
            totalSeats,
            activeLeases,
            availableSeats,
            Math.Round(util, 1),
            denials));
    }

    private static async Task<IResult> GetKeysAsync(
        Security.KeyManager keyManager,
        CancellationToken ct)
    {
        var keys = await keyManager.GetAllKeysAsync(ct).ConfigureAwait(false);
        var dtos = keys.Select(k => new SigningKeyDto(
            k.Id,
            k.TenantId,
            k.Kid,
            k.Alg,
            k.Role,
            k.State,
            k.NotBefore,
            k.NotAfter)).ToList();

        return TypedResults.Ok(dtos);
    }

    private static async Task<IResult> RotateKeyAsync(
        RotateKeyDto dto,
        HttpContext context,
        Security.KeyManager keyManager,
        SymbolonDbContext db,
        Webhooks.IWebhookDispatcher webhooks,
        CancellationToken ct)
    {
        string tenantId = dto.TenantId
            ?? context.Request.Headers["X-Tenant-Id"].FirstOrDefault()
            ?? await db.Tenants.Select(t => t.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false)
            ?? "default";

        string alg = dto.Alg ?? Crypto.Alg.Es256;
        var (entity, jwk) = await keyManager.RotateKeyAsync(tenantId, alg, ct).ConfigureAwait(false);

        var keyDto = new SigningKeyDto(
            entity.Id,
            entity.TenantId,
            entity.Kid,
            entity.Alg,
            entity.Role,
            entity.State,
            entity.NotBefore,
            entity.NotAfter);

        await webhooks.PublishEventAsync("key.rotated", new
        {
            kid = entity.Kid,
            tenantId = entity.TenantId,
            alg = entity.Alg,
            notBefore = entity.NotBefore,
            notAfter = entity.NotAfter
        }, entity.TenantId, ct).ConfigureAwait(false);

        return TypedResults.Created($"/admin/v1/keys/{entity.Kid}", new { Key = keyDto, Jwk = jwk });
    }

    private static async Task<IResult> RevokeKeyAsync(
        string kid,
        RevokeKeyDto dto,
        Security.KeyManager keyManager,
        CancellationToken ct)
    {
        bool revoked = await keyManager.RevokeKeyAsync(kid, dto.Reason, ct).ConfigureAwait(false);
        return revoked
            ? TypedResults.Ok(new { message = $"Key '{kid}' revoked successfully.", kid, reason = dto.Reason })
            : TypedResults.NotFound($"Key '{kid}' not found or already revoked.");
    }

    private static async Task<IResult> AssignLicenseUserAsync(
        string id,
        AssignLicenseUserDto dto,
        SymbolonDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var license = await db.Licenses.FirstOrDefaultAsync(l => l.Id == id, ct).ConfigureAwait(false);
        if (license is null) return TypedResults.NotFound();

        string userRecordId = $"usr_{Guid.NewGuid():N}";
        var existing = await db.LicenseUsers.FirstOrDefaultAsync(u => u.LicenseId == id && u.UserId == dto.UserId.Trim(), ct).ConfigureAwait(false);
        if (existing is not null)
        {
            existing.GroupName = dto.GroupName?.Trim();
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return TypedResults.Ok(new LicenseUserDto(existing.Id, existing.LicenseId, existing.UserId, existing.GroupName, existing.CreatedAt));
        }

        var user = new LicenseUserEntity
        {
            Id = userRecordId,
            LicenseId = id,
            UserId = dto.UserId.Trim(),
            GroupName = dto.GroupName?.Trim(),
            CreatedAt = time.GetUtcNow()
        };

        db.LicenseUsers.Add(user);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return TypedResults.Created($"/admin/v1/licenses/{id}/users/{user.UserId}",
            new LicenseUserDto(user.Id, user.LicenseId, user.UserId, user.GroupName, user.CreatedAt));
    }

    private static async Task<IResult> GetLicenseUsersAsync(
        string id,
        SymbolonDbContext db,
        CancellationToken ct)
    {
        var users = await db.LicenseUsers
            .Where(u => u.LicenseId == id)
            .OrderBy(u => u.CreatedAt)
            .Select(u => new LicenseUserDto(u.Id, u.LicenseId, u.UserId, u.GroupName, u.CreatedAt))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return TypedResults.Ok(users);
    }

    private static async Task<IResult> RemoveLicenseUserAsync(
        string id,
        string userId,
        SymbolonDbContext db,
        CancellationToken ct)
    {
        var user = await db.LicenseUsers.FirstOrDefaultAsync(u => u.LicenseId == id && (u.Id == userId || u.UserId == userId), ct).ConfigureAwait(false);
        if (user is null) return TypedResults.NotFound();

        db.LicenseUsers.Remove(user);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(new { message = $"User {userId} removed from license {id}." });
    }

    private static async Task<IResult> SetLicenseQuotaAsync(
        string id,
        SetLicenseQuotaDto dto,
        SymbolonDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var license = await db.Licenses.FirstOrDefaultAsync(l => l.Id == id, ct).ConfigureAwait(false);
        if (license is null) return TypedResults.NotFound();

        var existing = await db.LicenseQuotas.FirstOrDefaultAsync(q => q.LicenseId == id && q.EntitlementCode == dto.EntitlementCode.Trim(), ct).ConfigureAwait(false);
        var now = time.GetUtcNow();

        if (existing is not null)
        {
            existing.TotalUnits = dto.TotalUnits;
            existing.UpdatedAt = now;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return TypedResults.Ok(new LicenseQuotaAdminDto(
                existing.Id, existing.LicenseId, existing.EntitlementCode, existing.TotalUnits, existing.ConsumedUnits, existing.TotalUnits - existing.ConsumedUnits, existing.UpdatedAt));
        }

        var quota = new LicenseQuotaEntity
        {
            Id = $"qta_{Guid.NewGuid():N}",
            LicenseId = id,
            EntitlementCode = dto.EntitlementCode.Trim(),
            TotalUnits = dto.TotalUnits,
            ConsumedUnits = 0,
            UpdatedAt = now
        };

        db.LicenseQuotas.Add(quota);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return TypedResults.Created($"/admin/v1/licenses/{id}/quotas", new LicenseQuotaAdminDto(
            quota.Id, quota.LicenseId, quota.EntitlementCode, quota.TotalUnits, quota.ConsumedUnits, quota.TotalUnits, quota.UpdatedAt));
    }

    private static async Task<IResult> GetLicenseQuotasAsync(
        string id,
        SymbolonDbContext db,
        CancellationToken ct)
    {
        var quotas = await db.LicenseQuotas
            .Where(q => q.LicenseId == id)
            .OrderBy(q => q.EntitlementCode)
            .Select(q => new LicenseQuotaAdminDto(
                q.Id, q.LicenseId, q.EntitlementCode, q.TotalUnits, q.ConsumedUnits, q.TotalUnits - q.ConsumedUnits, q.UpdatedAt))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return TypedResults.Ok(quotas);
    }
}
