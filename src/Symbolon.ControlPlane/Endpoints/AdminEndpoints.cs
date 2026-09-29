using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Symbolon.ControlPlane.Models;
using Symbolon.Data;
using Symbolon.Data.Entities;
using Symbolon.Domain;
using Symbolon.Domain.PolicyRules;
using Symbolon.Domain.Reporting;
using Symbolon.Domain.Security;
using Symbolon.Format;
using Symbolon.Protocol;
using Symbolon.Protocol.Reporting;
using Symbolon.ControlPlane.Queuing;

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
        group.MapGet("/licenses/{id}/activations", GetLicenseActivationsAsync).WithName("GetLicenseActivations");
        group.MapDelete("/activations/{id}", DeactivateMachineAdminAsync).WithName("DeactivateMachineAdmin");

        // Audit, Reports & Alerts
        group.MapGet("/audit", GetAuditEventsAsync).WithName("GetAuditEvents");
        group.MapGet("/alerts", GetAlertsAsync).WithName("GetAlerts");
        group.MapGet("/reports/concurrency", GetConcurrencyReportAsync).WithName("GetConcurrencyReport");
        group.MapGet("/reports/concurrency/timeline", GetConcurrencyTimelineAsync).WithName("GetConcurrencyTimeline");
        group.MapGet("/reports/true-up", GetTrueUpReportAsync).WithName("GetTrueUpReport");
        group.MapGet("/reports/true-up/export", ExportTrueUpReportAsync).WithName("ExportTrueUpReport");
        group.MapGet("/reports/denials", GetDenialsReportAsync).WithName("GetDenialsReport");
        group.MapPost("/reports/audit/verify-integrity", VerifyAuditIntegrityAsync).WithName("VerifyAuditIntegrity");

        // Key Management & Rotation
        group.MapGet("/keys", GetKeysAsync).WithName("GetKeys");
        group.MapPost("/keys/rotate", RotateKeyAsync).WithName("RotateKey");
        group.MapPost("/keys/{kid}/revoke", RevokeKeyAsync).WithName("RevokeKey");

        // Revocation List Management
        group.MapGet("/revocations", GetRevocationsAdminAsync).WithName("GetRevocationsAdmin");
        group.MapPost("/revocations", CreateRevocationAdminAsync).WithName("CreateRevocationAdmin");

        // Named Users & Options
        group.MapPost("/licenses/{id}/users", AssignLicenseUserAsync).WithName("AssignLicenseUser");
        group.MapGet("/licenses/{id}/users", GetLicenseUsersAsync).WithName("GetLicenseUsers");
        group.MapDelete("/licenses/{id}/users/{userId}", RemoveLicenseUserAsync).WithName("RemoveLicenseUser");

        // Options File & Policy Rules (FLT-23, FLT-24, FLT-25, §7.5)
        group.MapGet("/licenses/{id}/rules", GetLicenseRulesAsync).WithName("GetLicenseRules");
        group.MapPut("/licenses/{id}/rules", UpdateLicenseRulesAsync).WithName("UpdateLicenseRules");
        group.MapPost("/licenses/{id}/rules/simulate", SimulateLicenseRulesAsync).WithName("SimulateLicenseRules");

        // Quotas & Metered Units
        group.MapPost("/licenses/{id}/quotas", SetLicenseQuotaAsync).WithName("SetLicenseQuota");
        group.MapGet("/licenses/{id}/quotas", GetLicenseQuotasAsync).WithName("GetLicenseQuotas");

        // Anti-Fraud Radar & License Borrowing Management
        group.MapGet("/fraud/radar", GetFraudRadarAsync).WithName("GetFraudRadar");
        group.MapGet("/leases/borrowed", GetBorrowedSeatsAsync).WithName("GetBorrowedSeats");
        group.MapPost("/leases/{id}/return", ReturnBorrowedSeatAdminAsync).WithName("ReturnBorrowedSeatAdmin");

        // Distributed Tracing Explorer
        group.MapGet("/traces/recent", GetRecentTraces).WithName("GetRecentTraces");

        // SCIM 2.0 Directory Management
        group.MapGet("/scim/users", GetScimUsersSummaryAsync).WithName("GetScimUsersSummary");

        // Enterprise License Queue Management (FLT-31)
        group.MapGet("/queue", GetQueueTicketsAdminAsync).WithName("GetQueueTicketsAdmin");
        group.MapPost("/queue/{ticket}/promote", PromoteQueueTicketAdminAsync).WithName("PromoteQueueTicketAdmin");
        group.MapDelete("/queue/{ticket}", CancelQueueTicketAdminAsync).WithName("CancelQueueTicketAdmin");

        group.AddEndpointFilter(async (invocationContext, next) =>
        {
            var http = invocationContext.HttpContext;
            if (http.User.Identity?.IsAuthenticated == true && http.User.IsInRole("auditor"))
            {
                if (!HttpMethods.IsGet(http.Request.Method) && !HttpMethods.IsHead(http.Request.Method) && !HttpMethods.IsOptions(http.Request.Method))
                {
                    return Results.Problem(
                        statusCode: StatusCodes.Status403Forbidden,
                        title: "Auditor Access Denied",
                        detail: "Auditor role has read-only permissions and cannot modify resources.",
                        type: Symbolon.Protocol.ProblemTypes.Forbidden);
                }
            }
            return await next(invocationContext).ConfigureAwait(false);
        });

        return group;
    }

    private static string? GetCallerTenantId(HttpContext httpContext)
    {
        return httpContext.User.FindFirst("tenant_id")?.Value;
    }

    private static bool IsSuperAdmin(HttpContext httpContext)
    {
        return httpContext.User.IsInRole("admin:super");
    }

    private static string? GetEffectiveTenantFilter(HttpContext httpContext)
    {
        if (IsSuperAdmin(httpContext))
        {
            return httpContext.Request.Headers["X-Tenant-Id"].FirstOrDefault()
                ?? GetCallerTenantId(httpContext);
        }
        return GetCallerTenantId(httpContext);
    }

    private static async Task<IResult> CreateTenantAsync(
        CreateTenantDto dto,
        HttpContext context,
        SymbolonDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        if (!IsSuperAdmin(context))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "Forbidden",
                detail: "Only super administrators can create new tenants.");
        }

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

    private static async Task<IResult> GetTenantsAsync(
        HttpContext context,
        SymbolonDbContext db,
        CancellationToken ct)
    {
        if (IsSuperAdmin(context))
        {
            var list = await db.Tenants
                .Select(t => new TenantDto(t.Id, t.Slug, t.Name, t.CreatedAt))
                .ToListAsync(ct)
                .ConfigureAwait(false);
            return TypedResults.Ok(list);
        }

        string? tenantId = GetCallerTenantId(context);
        var single = await db.Tenants
            .Where(t => t.Id == tenantId)
            .Select(t => new TenantDto(t.Id, t.Slug, t.Name, t.CreatedAt))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return TypedResults.Ok(single);
    }

    private static async Task<IResult> CreateProductAsync(
        CreateProductDto dto,
        HttpContext context,
        SymbolonDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        string? tenantId;
        if (IsSuperAdmin(context))
        {
            tenantId = context.Request.Headers["X-Tenant-Id"].FirstOrDefault()
                ?? GetCallerTenantId(context);
        }
        else
        {
            tenantId = GetCallerTenantId(context);
        }

        tenantId ??= (await db.Tenants.Select(t => t.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false)) ?? "default";

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

    private static async Task<IResult> GetProductsAsync(
        HttpContext context,
        SymbolonDbContext db,
        CancellationToken ct)
    {
        string? tenantFilter = GetEffectiveTenantFilter(context);
        var query = db.Products.AsQueryable();
        if (!IsSuperAdmin(context) && !string.IsNullOrWhiteSpace(tenantFilter))
        {
            query = query.Where(p => p.TenantId == tenantFilter);
        }

        var list = await query
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

        if (!IsSuperAdmin(context) && product.TenantId != GetCallerTenantId(context))
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
            BorrowMaxConcurrent = dto.BorrowMaxConcurrent,
            OfflineAllowed = dto.OfflineAllowed,
            CryptoProfile = dto.CryptoProfile,
            MachineMatching = dto.MachineMatching,
            MachineUniqueness = dto.MachineUniqueness,
            EntitlementsJson = JsonSerializer.Serialize(dto.Entitlements ?? ["core"])
        };

        db.Policies.Add(policy);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return TypedResults.Created($"/admin/v1/policies/{id}", new PolicyDto(policy.Id, policy.TenantId, policy.ProductId, policy.Code, policy.Name, policy.LicenseModel, policy.MaxSeats, policy.SeatUnit, policy.LeaseTtlSeconds, policy.MachineMatching, policy.MachineUniqueness));
    }

    private static async Task<IResult> GetPoliciesAsync(
        HttpContext context,
        SymbolonDbContext db,
        CancellationToken ct)
    {
        string? tenantFilter = GetEffectiveTenantFilter(context);
        var query = db.Policies.AsQueryable();
        if (!IsSuperAdmin(context) && !string.IsNullOrWhiteSpace(tenantFilter))
        {
            query = query.Where(p => p.TenantId == tenantFilter);
        }

        var list = await query
            .Select(p => new PolicyDto(p.Id, p.TenantId, p.ProductId, p.Code, p.Name, p.LicenseModel, p.MaxSeats, p.SeatUnit, p.LeaseTtlSeconds, p.MachineMatching, p.MachineUniqueness))
            .ToListAsync(ct)
            .ConfigureAwait(false);
        return TypedResults.Ok(list);
    }

    private static async Task<IResult> IssueLicenseAsync(
        CreateLicenseDto dto,
        HttpContext context,
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

        if (!IsSuperAdmin(context) && policy.TenantId != GetCallerTenantId(context))
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

    private static async Task<IResult> GetLicensesAsync(
        HttpContext context,
        SymbolonDbContext db,
        CancellationToken ct)
    {
        string? tenantFilter = GetEffectiveTenantFilter(context);
        var query = db.Licenses.AsQueryable();
        if (!IsSuperAdmin(context) && !string.IsNullOrWhiteSpace(tenantFilter))
        {
            query = query.Where(l => l.TenantId == tenantFilter);
        }

        var list = await query
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

    private static async Task<IResult> GetLicenseByIdAsync(
        string id,
        HttpContext context,
        SymbolonDbContext db,
        CancellationToken ct)
    {
        var l = await db.Licenses.FirstOrDefaultAsync(x => x.Id == id, ct).ConfigureAwait(false);
        if (l is null) return TypedResults.NotFound();

        if (!IsSuperAdmin(context) && l.TenantId != GetCallerTenantId(context))
        {
            return TypedResults.NotFound();
        }

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
        HttpContext context,
        SymbolonDbContext db,
        IAuditLedger audit,
        Webhooks.IWebhookDispatcher webhooks,
        TimeProvider time,
        CancellationToken ct)
    {
        var license = await db.Licenses.FirstOrDefaultAsync(l => l.Id == id, ct).ConfigureAwait(false);
        if (license is null) return TypedResults.NotFound();

        if (!IsSuperAdmin(context) && license.TenantId != GetCallerTenantId(context))
        {
            return TypedResults.NotFound();
        }

        license.State = "revoked";

        var now = time.GetUtcNow();
        long nextSeq = (await db.Revocations.MaxAsync(r => (long?)r.Sequence, ct).ConfigureAwait(false) ?? 0) + 1;
        var rev = new RevocationEntity
        {
            Id = $"rev_{Guid.NewGuid():N}",
            TenantId = license.TenantId,
            SubjectType = "license",
            SubjectId = license.Id,
            Reason = dto.Reason,
            RevokedAt = now,
            Sequence = nextSeq
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

    private static async Task<IResult> GetLicenseActivationsAsync(
        string id,
        HttpContext context,
        SymbolonDbContext db,
        CancellationToken ct)
    {
        var license = await db.Licenses
            .Include(l => l.Machines)
            .FirstOrDefaultAsync(l => l.Id == id, ct)
            .ConfigureAwait(false);

        if (license is null) return TypedResults.NotFound();

        if (!IsSuperAdmin(context) && license.TenantId != GetCallerTenantId(context))
        {
            return TypedResults.NotFound();
        }

        var dtos = license.Machines.Select(m =>
        {
            Dictionary<string, string>? components = null;
            if (!string.IsNullOrWhiteSpace(m.ComponentsJson))
            {
                try
                {
                    components = JsonSerializer.Deserialize<Dictionary<string, string>>(m.ComponentsJson);
                }
                catch (JsonException)
                {
                }
            }

            return new MachineActivationAdminDto(
                m.Id,
                m.LicenseId,
                m.Fingerprint,
                m.Id,
                m.State,
                m.FirstSeen,
                m.LastHeartbeat,
                components);
        }).ToList();

        return TypedResults.Ok(dtos);
    }

    private static async Task<IResult> DeactivateMachineAdminAsync(
        string id,
        HttpContext context,
        SymbolonDbContext db,
        IAuditLedger audit,
        TimeProvider time,
        CancellationToken ct)
    {
        var machine = await db.Machines
            .Include(m => m.License)
            .FirstOrDefaultAsync(m => m.Id == id, ct)
            .ConfigureAwait(false);

        if (machine is null) return TypedResults.NotFound();

        if (!IsSuperAdmin(context) && machine.License?.TenantId != GetCallerTenantId(context))
        {
            return TypedResults.NotFound();
        }

        machine.State = "deactivated";
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var now = time.GetUtcNow();
        await audit.AppendAsync(new AuditEvent("deactivate", machine.LicenseId, null, machine.Fingerprint, now, $"Admin deactivated machine node-lock: {machine.Id}"), ct).ConfigureAwait(false);

        return TypedResults.Ok(new { success = true, id = machine.Id });
    }

    private static async Task<IResult> GetAuditEventsAsync(
        string? licenseId,
        int? limit,
        HttpContext context,
        SymbolonDbContext db,
        CancellationToken ct)
    {
        int max = limit ?? 50;
        var query = db.AuditEvents.AsQueryable();

        if (!IsSuperAdmin(context))
        {
            string? callerTenant = GetCallerTenantId(context);
            if (!string.IsNullOrWhiteSpace(callerTenant))
            {
                var tenantLicenseIds = db.Licenses.Where(l => l.TenantId == callerTenant).Select(l => l.Id);
                query = query.Where(a => a.LicenseId != null && tenantLicenseIds.Contains(a.LicenseId));
            }
        }

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

    private static async Task<IResult> GetAlertsAsync(
        HttpContext context,
        SymbolonDbContext db,
        CancellationToken ct)
    {
        var query = db.AuditEvents.Where(a => a.Type.StartsWith("alert."));

        if (!IsSuperAdmin(context))
        {
            string? callerTenant = GetCallerTenantId(context);
            if (!string.IsNullOrWhiteSpace(callerTenant))
            {
                query = query.Where(a => a.TenantId == callerTenant);
            }
        }

        var alerts = await query
            .OrderByDescending(a => a.TsServer)
            .Take(50)
            .Select(a => new
            {
                id = a.Id,
                type = a.Type,
                licenseId = a.LicenseId,
                tenantId = a.TenantId,
                subject = a.Subject,
                payload = a.PayloadJson,
                timestamp = a.TsServer
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return TypedResults.Ok(alerts);
    }

    private static async Task<IResult> GetConcurrencyReportAsync(
        HttpContext context,
        SymbolonDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var now = time.GetUtcNow();
        string? bucket = context.Request.Query["bucket"];
        if (!string.IsNullOrWhiteSpace(bucket))
        {
            return await GetConcurrencyTimelineAsync(context, db, time, ct).ConfigureAwait(false);
        }

        var seatsQuery = db.Seats.AsQueryable();

        if (!IsSuperAdmin(context))
        {
            string? callerTenant = GetCallerTenantId(context);
            if (!string.IsNullOrWhiteSpace(callerTenant))
            {
                var tenantLicenseIds = db.Licenses.Where(l => l.TenantId == callerTenant).Select(l => l.Id);
                seatsQuery = seatsQuery.Where(s => tenantLicenseIds.Contains(s.LicenseId));
            }
        }

        int totalSeats = await seatsQuery.CountAsync(ct).ConfigureAwait(false);
        int activeLeases = await seatsQuery.CountAsync(s => s.LeaseId != null && s.ExpiresAt > now, ct).ConfigureAwait(false);
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

    private static async Task<IResult> GetConcurrencyTimelineAsync(
        HttpContext context,
        SymbolonDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var now = time.GetUtcNow();
        string bucket = context.Request.Query["bucket"].FirstOrDefault() ?? "hour";
        string? licenseId = context.Request.Query["licenseId"];

        DateTimeOffset rangeStart = DateTimeOffset.TryParse(context.Request.Query["from"], out var parsedFrom)
            ? parsedFrom
            : now.AddDays(-7);

        DateTimeOffset rangeEnd = DateTimeOffset.TryParse(context.Request.Query["to"], out var parsedTo)
            ? parsedTo
            : now;

        var auditQuery = db.AuditEvents.Where(a => a.TsServer >= rangeStart && a.TsServer <= rangeEnd);
        var seatsQuery = db.Seats.AsQueryable();

        if (!IsSuperAdmin(context))
        {
            string? callerTenant = GetCallerTenantId(context);
            if (!string.IsNullOrWhiteSpace(callerTenant))
            {
                var tenantLicenseIds = db.Licenses.Where(l => l.TenantId == callerTenant).Select(l => l.Id);
                auditQuery = auditQuery.Where(a => a.TenantId == callerTenant || (a.LicenseId != null && tenantLicenseIds.Contains(a.LicenseId)));
                seatsQuery = seatsQuery.Where(s => tenantLicenseIds.Contains(s.LicenseId));
            }
        }

        if (!string.IsNullOrWhiteSpace(licenseId))
        {
            auditQuery = auditQuery.Where(a => a.LicenseId == licenseId);
            seatsQuery = seatsQuery.Where(s => s.LicenseId == licenseId);
        }

        int capacity = await seatsQuery.CountAsync(ct).ConfigureAwait(false);
        var rawEvents = await auditQuery.OrderBy(a => a.TsServer).ThenBy(a => a.Id).ToListAsync(ct).ConfigureAwait(false);

        var domainEvents = rawEvents.Select(a => new ConcurrencyAuditEvent(
            a.Id,
            a.TsServer,
            a.Type,
            a.LicenseId,
            a.Subject
        )).ToList();

        var result = AuditPeakConcurrencyCalculator.Calculate(
            domainEvents,
            rangeStart,
            rangeEnd,
            bucket,
            licenseId,
            capacity,
            initialActiveSeats: 0
        );

        return TypedResults.Ok(result);
    }

    private static async Task<IResult> GetTrueUpReportAsync(
        HttpContext context,
        SymbolonDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var now = time.GetUtcNow();
        string? licenseId = context.Request.Query["licenseId"];

        DateTimeOffset rangeStart = DateTimeOffset.TryParse(context.Request.Query["from"], out var parsedFrom)
            ? parsedFrom
            : now.AddDays(-30);

        DateTimeOffset rangeEnd = DateTimeOffset.TryParse(context.Request.Query["to"], out var parsedTo)
            ? parsedTo
            : now;

        string? callerTenant = GetCallerTenantId(context);
        var licenseQuery = db.Licenses.Include(l => l.Policy)!.ThenInclude(p => p!.Product).AsQueryable();
        if (!IsSuperAdmin(context) && !string.IsNullOrWhiteSpace(callerTenant))
        {
            licenseQuery = licenseQuery.Where(l => l.TenantId == callerTenant);
        }

        if (!string.IsNullOrWhiteSpace(licenseId))
        {
            licenseQuery = licenseQuery.Where(l => l.Id == licenseId);
        }

        var license = await licenseQuery.FirstOrDefaultAsync(ct).ConfigureAwait(false);
        string reportTenantId = license?.TenantId ?? callerTenant ?? "default";
        int licensedSeats = license?.MaxSeats ?? 10;
        string prodName = license?.Policy?.Product?.Name ?? "Symbolon Enterprise Suite";
        int overageBuffer = 0;
        if (license?.Policy != null && !string.Equals(license.Policy.OverageStrategy, "no-overage", StringComparison.OrdinalIgnoreCase))
        {
            overageBuffer = Math.Max(1, (int)(licensedSeats * 0.2));
        }

        var auditQuery = db.AuditEvents.Where(a => a.TsServer >= rangeStart && a.TsServer <= rangeEnd);
        if (!IsSuperAdmin(context) && !string.IsNullOrWhiteSpace(callerTenant))
        {
            auditQuery = auditQuery.Where(a => a.TenantId == callerTenant);
        }
        if (!string.IsNullOrWhiteSpace(licenseId))
        {
            auditQuery = auditQuery.Where(a => a.LicenseId == licenseId);
        }

        var rawEvents = await auditQuery.OrderBy(a => a.TsServer).ThenBy(a => a.Id).ToListAsync(ct).ConfigureAwait(false);
        var domainEvents = rawEvents.Select(a => new ConcurrencyAuditEvent(
            a.Id,
            a.TsServer,
            a.Type,
            a.LicenseId,
            a.Subject
        )).ToList();

        var report = TrueUpReportGenerator.Generate(
            reportTenantId,
            licenseId,
            prodName,
            licensedSeats,
            overageBuffer,
            domainEvents,
            rangeStart,
            rangeEnd,
            now
        );

        return TypedResults.Ok(report);
    }

    private static async Task<IResult> ExportTrueUpReportAsync(
        HttpContext context,
        SymbolonDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        string format = context.Request.Query["format"].FirstOrDefault() ?? "csv";
        var res = await GetTrueUpReportAsync(context, db, time, ct).ConfigureAwait(false);

        if (res is Microsoft.AspNetCore.Http.HttpResults.Ok<TrueUpReportDto> ok && ok.Value is { } report)
        {
            if (string.Equals(format, "json", StringComparison.OrdinalIgnoreCase))
            {
                return TypedResults.Ok(report);
            }

            string csv = TrueUpReportGenerator.ExportToCsv(report);
            return TypedResults.Content(csv, "text/csv; charset=utf-8", Encoding.UTF8);
        }

        return res;
    }

    private static async Task<IResult> GetDenialsReportAsync(
        HttpContext context,
        SymbolonDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var now = time.GetUtcNow();
        string? licenseId = context.Request.Query["licenseId"];

        DateTimeOffset rangeStart = DateTimeOffset.TryParse(context.Request.Query["from"], out var parsedFrom)
            ? parsedFrom
            : now.AddDays(-30);

        DateTimeOffset rangeEnd = DateTimeOffset.TryParse(context.Request.Query["to"], out var parsedTo)
            ? parsedTo
            : now;

        int limit = int.TryParse(context.Request.Query["limit"], out var parsedLimit)
            ? Math.Clamp(parsedLimit, 1, 500)
            : 50;

        var auditQuery = db.AuditEvents.Where(a => a.Type == "deny" && a.TsServer >= rangeStart && a.TsServer <= rangeEnd);

        if (!IsSuperAdmin(context))
        {
            string? callerTenant = GetCallerTenantId(context);
            if (!string.IsNullOrWhiteSpace(callerTenant))
            {
                var tenantLicenseIds = db.Licenses.Where(l => l.TenantId == callerTenant).Select(l => l.Id);
                auditQuery = auditQuery.Where(a => a.TenantId == callerTenant || (a.LicenseId != null && tenantLicenseIds.Contains(a.LicenseId)));
            }
        }

        if (!string.IsNullOrWhiteSpace(licenseId))
        {
            auditQuery = auditQuery.Where(a => a.LicenseId == licenseId);
        }

        var rawEvents = await auditQuery.OrderByDescending(a => a.TsServer).ToListAsync(ct).ConfigureAwait(false);

        var reasons = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var recent = new List<DenialRecordDto>();
        var uniqueSubjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var ev in rawEvents)
        {
            if (!string.IsNullOrWhiteSpace(ev.Subject))
            {
                uniqueSubjects.Add(ev.Subject);
            }

            string reason = "seat-pool-exhausted";
            string? feat = null;
            if (!string.IsNullOrWhiteSpace(ev.PayloadJson))
            {
                try
                {
                    using var doc = JsonDocument.Parse(ev.PayloadJson);
                    if (doc.RootElement.TryGetProperty("Detail", out var detailEl) && detailEl.ValueKind == JsonValueKind.String)
                    {
                        reason = detailEl.GetString() ?? reason;
                    }
                    else if (doc.RootElement.TryGetProperty("reason", out var rEl) && rEl.ValueKind == JsonValueKind.String)
                    {
                        reason = rEl.GetString() ?? reason;
                    }

                    if (doc.RootElement.TryGetProperty("feature", out var fEl) && fEl.ValueKind == JsonValueKind.String)
                    {
                        feat = fEl.GetString();
                    }
                }
                catch (JsonException)
                {
                    // keep default
                }
            }

            reasons[reason] = reasons.GetValueOrDefault(reason) + 1;

            if (recent.Count < limit)
            {
                recent.Add(new DenialRecordDto(
                    ev.TsServer,
                    ev.LicenseId ?? string.Empty,
                    ev.Subject,
                    reason,
                    feat,
                    ev.Subject
                ));
            }
        }

        return TypedResults.Ok(new DenialsAnalyticsResponseDto(
            rangeStart,
            rangeEnd,
            rawEvents.Count,
            uniqueSubjects.Count,
            reasons,
            recent
        ));
    }

    private static async Task<IResult> VerifyAuditIntegrityAsync(
        HttpContext context,
        SymbolonDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var auditQuery = db.AuditEvents.AsQueryable();

        if (!IsSuperAdmin(context))
        {
            string? callerTenant = GetCallerTenantId(context);
            if (!string.IsNullOrWhiteSpace(callerTenant))
            {
                auditQuery = auditQuery.Where(a => a.TenantId == callerTenant);
            }
        }

        var rawEvents = await auditQuery
            .OrderBy(a => a.TsServer)
            .ThenBy(a => a.Id)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var items = rawEvents.Select(a => new AuditRecordItem(
            a.Id,
            a.TsServer,
            a.Type,
            a.LicenseId,
            a.Subject,
            a.PayloadJson,
            a.PrevHash,
            a.Hash
        )).ToList();

        var proof = AuditChainIntegrityVerifier.Verify(items, now);
        return TypedResults.Ok(proof);
    }

    private static async Task<IResult> GetKeysAsync(
        HttpContext context,
        Security.KeyManager keyManager,
        CancellationToken ct)
    {
        if (!IsSuperAdmin(context))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "SuperAdmin Required");
        }

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
        if (!IsSuperAdmin(context))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "SuperAdmin Required");
        }

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
        HttpContext context,
        Security.KeyManager keyManager,
        CancellationToken ct)
    {
        if (!IsSuperAdmin(context))
        {
            return TypedResults.Problem(statusCode: StatusCodes.Status403Forbidden, title: "SuperAdmin Required");
        }

        bool revoked = await keyManager.RevokeKeyAsync(kid, dto.Reason, ct).ConfigureAwait(false);
        return revoked
            ? TypedResults.Ok(new { message = $"Key '{kid}' revoked successfully.", kid, reason = dto.Reason })
            : TypedResults.NotFound($"Key '{kid}' not found or already revoked.");
    }

    private static async Task<IResult> AssignLicenseUserAsync(
        string id,
        AssignLicenseUserDto dto,
        HttpContext context,
        SymbolonDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var license = await db.Licenses.FirstOrDefaultAsync(l => l.Id == id, ct).ConfigureAwait(false);
        if (license is null) return TypedResults.NotFound();

        if (!IsSuperAdmin(context) && license.TenantId != GetCallerTenantId(context))
        {
            return TypedResults.NotFound();
        }

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
        HttpContext context,
        SymbolonDbContext db,
        CancellationToken ct)
    {
        var license = await db.Licenses.FirstOrDefaultAsync(l => l.Id == id, ct).ConfigureAwait(false);
        if (license is null) return TypedResults.NotFound();

        if (!IsSuperAdmin(context) && license.TenantId != GetCallerTenantId(context))
        {
            return TypedResults.NotFound();
        }

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
        HttpContext context,
        SymbolonDbContext db,
        CancellationToken ct)
    {
        var license = await db.Licenses.FirstOrDefaultAsync(l => l.Id == id, ct).ConfigureAwait(false);
        if (license is null) return TypedResults.NotFound();

        if (!IsSuperAdmin(context) && license.TenantId != GetCallerTenantId(context))
        {
            return TypedResults.NotFound();
        }

        var user = await db.LicenseUsers.FirstOrDefaultAsync(u => u.LicenseId == id && (u.Id == userId || u.UserId == userId), ct).ConfigureAwait(false);
        if (user is null) return TypedResults.NotFound();

        db.LicenseUsers.Remove(user);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return TypedResults.Ok(new { message = $"User {userId} removed from license {id}." });
    }

    private static async Task<IResult> GetLicenseRulesAsync(
        string id,
        HttpContext context,
        SymbolonDbContext db,
        CancellationToken ct)
    {
        var license = await db.Licenses.Include(l => l.Policy).FirstOrDefaultAsync(l => l.Id == id, ct).ConfigureAwait(false);
        if (license is null) return TypedResults.NotFound();

        if (!IsSuperAdmin(context) && license.TenantId != GetCallerTenantId(context))
        {
            return TypedResults.NotFound();
        }

        string rawYaml = license.RulesYaml ?? license.Policy?.RulesYaml ?? string.Empty;
        var ruleSet = !string.IsNullOrWhiteSpace(rawYaml)
            ? PolicyRuleSerializer.Parse(rawYaml)
            : new PolicyRuleSet(1, license.Id, [], []);

        var dto = new PolicyRuleSetDto
        {
            Version = ruleSet.Version,
            LicenseId = license.Id,
            RawYaml = rawYaml,
            Groups = ruleSet.Groups.Select(g => new PolicyRuleGroupDto
            {
                Name = g.Name,
                Members = g.Members,
                Hosts = g.Hosts
            }).ToList(),
            Rules = ruleSet.Rules.Select(r => new PolicyRuleItemDto
            {
                Type = r.Type switch
                {
                    PolicyRuleType.Deny => "deny",
                    PolicyRuleType.Max => "max",
                    PolicyRuleType.Reserve => "reserve",
                    PolicyRuleType.Priority => "priority",
                    _ => "deny"
                },
                Value = r.Value,
                Group = r.Target?.Group,
                User = r.Target?.User,
                Subnet = r.Target?.Subnet,
                Host = r.Target?.Host,
                Hosts = r.Hosts,
                Users = r.Users,
                Groups = r.Groups,
                Subnets = r.Subnets,
                Feature = r.Feature,
                Reason = r.Reason
            }).ToList()
        };

        return TypedResults.Ok(dto);
    }

    private static async Task<IResult> UpdateLicenseRulesAsync(
        string id,
        UpdatePolicyRulesRequestDto dto,
        HttpContext context,
        SymbolonDbContext db,
        ISeatStore seatStore,
        IAuditLedger audit,
        TimeProvider time,
        CancellationToken ct)
    {
        var license = await db.Licenses.Include(l => l.Policy).FirstOrDefaultAsync(l => l.Id == id, ct).ConfigureAwait(false);
        if (license is null) return TypedResults.NotFound();

        if (!IsSuperAdmin(context) && license.TenantId != GetCallerTenantId(context))
        {
            return TypedResults.NotFound();
        }

        string? content = dto.RulesYaml ?? dto.RulesJson;
        PolicyRuleSet ruleSet;
        if (string.IsNullOrWhiteSpace(content))
        {
            license.RulesYaml = null;
            ruleSet = new PolicyRuleSet(1, license.Id, [], []);
            await seatStore.SyncSeatReservationsAsync(license.Id, [], ct).ConfigureAwait(false);
        }
        else
        {
            try
            {
                ruleSet = PolicyRuleSerializer.Parse(content);
            }
            catch (Exception ex) when (ex is JsonException or FormatException or ArgumentException)
            {
                return TypedResults.Problem(
                    statusCode: StatusCodes.Status400BadRequest,
                    title: "Invalid Policy Rules Format",
                    detail: ex.Message,
                    type: ProblemTypes.InvalidRequest);
            }

            license.RulesYaml = PolicyRuleSerializer.ToYaml(ruleSet);

            var reservations = ruleSet.Rules
                .Where(r => r.Type == PolicyRuleType.Reserve && r.Value.HasValue)
                .Select(r => (Target: r.Target?.Group ?? r.Target?.User ?? (r.Groups is { Count: > 0 } ? r.Groups[0] : (r.Users is { Count: > 0 } ? r.Users[0] : "default")), Count: r.Value.GetValueOrDefault()))
                .ToList();

            await seatStore.SyncSeatReservationsAsync(license.Id, reservations, ct).ConfigureAwait(false);
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var now = time.GetUtcNow();
        await audit.AppendAsync(new AuditEvent(
            "policy.rules_updated",
            license.Id,
            null,
            null,
            now,
            $"Updated policy rules: {ruleSet.Rules.Count} rules across {ruleSet.Groups.Count} groups"), ct).ConfigureAwait(false);

        var resultDto = new PolicyRuleSetDto
        {
            Version = ruleSet.Version,
            LicenseId = license.Id,
            RawYaml = license.RulesYaml,
            Groups = ruleSet.Groups.Select(g => new PolicyRuleGroupDto
            {
                Name = g.Name,
                Members = g.Members,
                Hosts = g.Hosts
            }).ToList(),
            Rules = ruleSet.Rules.Select(r => new PolicyRuleItemDto
            {
                Type = r.Type switch
                {
                    PolicyRuleType.Deny => "deny",
                    PolicyRuleType.Max => "max",
                    PolicyRuleType.Reserve => "reserve",
                    PolicyRuleType.Priority => "priority",
                    _ => "deny"
                },
                Value = r.Value,
                Group = r.Target?.Group,
                User = r.Target?.User,
                Subnet = r.Target?.Subnet,
                Host = r.Target?.Host,
                Hosts = r.Hosts,
                Users = r.Users,
                Groups = r.Groups,
                Subnets = r.Subnets,
                Feature = r.Feature,
                Reason = r.Reason
            }).ToList()
        };

        return TypedResults.Ok(resultDto);
    }

    private static async Task<IResult> SimulateLicenseRulesAsync(
        string id,
        SimulateRuleEvaluationRequestDto dto,
        HttpContext context,
        SymbolonDbContext db,
        CancellationToken ct)
    {
        var license = await db.Licenses.Include(l => l.Policy).FirstOrDefaultAsync(l => l.Id == id, ct).ConfigureAwait(false);
        if (license is null) return TypedResults.NotFound();

        if (!IsSuperAdmin(context) && license.TenantId != GetCallerTenantId(context))
        {
            return TypedResults.NotFound();
        }

        string rawYaml = license.RulesYaml ?? license.Policy?.RulesYaml ?? string.Empty;
        if (string.IsNullOrWhiteSpace(rawYaml))
        {
            return TypedResults.Ok(new SimulateRuleEvaluationResponseDto
            {
                Allowed = true,
                DenyReason = null,
                DenyType = null,
                ResolvedPriority = null,
                MatchedReservation = null,
                MaxLimit = null
            });
        }

        var ruleSet = PolicyRuleSerializer.Parse(rawYaml);
        var activeSeats = await db.Seats
            .Where(s => s.LicenseId == license.Id && s.LeaseId != null)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        int GetActiveCount(string targetName)
        {
            if (targetName.StartsWith("group:", StringComparison.OrdinalIgnoreCase))
            {
                string grp = targetName["group:".Length..];
                var groupDef = ruleSet.Groups.FirstOrDefault(g => string.Equals(g.Name, grp, StringComparison.OrdinalIgnoreCase));
                if (groupDef != null)
                {
                    return activeSeats.Count(s =>
                        (s.UserId != null && groupDef.Members.Any(m => WildcardMatcher.Matches(s.UserId, m))) ||
                        (s.MachineId != null && groupDef.Hosts != null && groupDef.Hosts.Any(h => WildcardMatcher.Matches(s.MachineId, h))) ||
                        (s.ReservedFor != null && string.Equals(s.ReservedFor, grp, StringComparison.OrdinalIgnoreCase)));
                }
                return activeSeats.Count(s => (s.ReservedFor != null && string.Equals(s.ReservedFor, grp, StringComparison.OrdinalIgnoreCase)) ||
                                             (s.UserId != null && string.Equals(s.UserId, grp, StringComparison.OrdinalIgnoreCase)));
            }
            else if (targetName.StartsWith("user:", StringComparison.OrdinalIgnoreCase))
            {
                string u = targetName["user:".Length..];
                return activeSeats.Count(s => s.UserId != null && WildcardMatcher.Matches(s.UserId, u));
            }
            return 0;
        }

        var evalContext = new RuleEvaluationContext(
            LicenseId: license.Id,
            UserId: dto.UserId,
            MachineId: dto.MachineId,
            HostName: dto.HostName ?? dto.MachineId,
            ClientIp: dto.ClientIp,
            Features: dto.Features,
            CurrentlyHeldByClient: 0,
            GetActiveCountForTarget: GetActiveCount);

        var evalResult = PolicyRuleEngine.Evaluate(ruleSet, evalContext);

        return TypedResults.Ok(new SimulateRuleEvaluationResponseDto
        {
            Allowed = evalResult.Allowed,
            DenyReason = evalResult.DenyReason,
            DenyType = evalResult.DenyType,
            ResolvedPriority = evalResult.ResolvedPriority,
            MatchedReservation = evalResult.MatchedReservationTarget,
            MaxLimit = evalResult.MaxLimit
        });
    }

    private static async Task<IResult> SetLicenseQuotaAsync(
        string id,
        SetLicenseQuotaDto dto,
        HttpContext context,
        SymbolonDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var license = await db.Licenses.FirstOrDefaultAsync(l => l.Id == id, ct).ConfigureAwait(false);
        if (license is null) return TypedResults.NotFound();

        if (!IsSuperAdmin(context) && license.TenantId != GetCallerTenantId(context))
        {
            return TypedResults.NotFound();
        }

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
        HttpContext context,
        SymbolonDbContext db,
        CancellationToken ct)
    {
        var license = await db.Licenses.FirstOrDefaultAsync(l => l.Id == id, ct).ConfigureAwait(false);
        if (license is null) return TypedResults.NotFound();

        if (!IsSuperAdmin(context) && license.TenantId != GetCallerTenantId(context))
        {
            return TypedResults.NotFound();
        }

        var quotas = await db.LicenseQuotas
            .Where(q => q.LicenseId == id)
            .OrderBy(q => q.EntitlementCode)
            .Select(q => new LicenseQuotaAdminDto(
                q.Id, q.LicenseId, q.EntitlementCode, q.TotalUnits, q.ConsumedUnits, q.TotalUnits - q.ConsumedUnits, q.UpdatedAt))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return TypedResults.Ok(quotas);
    }

    private static Task<IResult> GetFraudRadarAsync(
        IFraudDetectionService fraudDetection,
        CancellationToken ct)
    {
        var anomalies = fraudDetection.GetRecentAnomalies(50);
        var dtos = anomalies.Select(a => new FraudRadarAdminDto(
            a.Id,
            a.LicenseId,
            a.UserId,
            a.MachineId,
            a.IpAddress,
            a.RiskType,
            a.RiskLevel.ToString(),
            a.Description,
            a.VelocityKmH,
            a.DistanceKm,
            a.Timestamp)).ToList();

        return Task.FromResult<IResult>(TypedResults.Ok(dtos));
    }

    private static async Task<IResult> GetBorrowedSeatsAsync(
        HttpContext context,
        SymbolonDbContext db,
        TimeProvider time,
        CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var query = db.Seats
            .Include(s => s.License)
            .Where(s => s.BorrowedUntil != null && s.BorrowedUntil > now);

        if (!IsSuperAdmin(context))
        {
            string? callerTenant = GetCallerTenantId(context);
            if (!string.IsNullOrWhiteSpace(callerTenant))
            {
                query = query.Where(s => s.License != null && s.License.TenantId == callerTenant);
            }
        }

        var seats = await query.ToListAsync(ct).ConfigureAwait(false);

        var dtos = seats.Select(s => new BorrowedSeatAdminDto(
            s.LeaseId ?? string.Empty,
            s.LicenseId,
            s.SeatNo,
            s.MachineId,
            s.BorrowedUntil!.Value,
            Math.Max(0.0, Math.Round((s.BorrowedUntil.Value - now).TotalHours, 1)))).ToList();

        return TypedResults.Ok(dtos);
    }

    private static async Task<IResult> ReturnBorrowedSeatAdminAsync(
        string id,
        HttpContext context,
        SymbolonDbContext db,
        LeaseEngine engine,
        Observability.SymbolonMetrics metrics,
        CancellationToken ct)
    {
        var seat = await db.Seats
            .Include(s => s.License)
            .FirstOrDefaultAsync(s => s.LeaseId == id, ct)
            .ConfigureAwait(false);

        if (seat is null)
        {
            return TypedResults.NotFound();
        }

        if (!IsSuperAdmin(context) && seat.License?.TenantId != GetCallerTenantId(context))
        {
            return TypedResults.NotFound();
        }

        seat.LeaseId = null;
        seat.HolderFp = null;
        seat.MachineId = null;
        seat.AcquiredAt = null;
        seat.ExpiresAt = null;
        seat.BorrowedUntil = null;
        seat.PossessionKey = null;
        seat.UserId = null;
        seat.LeaseSeq = 0;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        metrics.RecordSeatReleased(1);

        return TypedResults.Ok(new { success = true, leaseId = id });
    }

    private static IResult GetRecentTraces(
        Observability.SymbolonTraceBuffer traceBuffer)
    {
        return TypedResults.Ok(traceBuffer.GetRecentSpans());
    }

    private static async Task<IResult> GetScimUsersSummaryAsync(
        HttpContext httpContext,
        SymbolonDbContext db,
        CancellationToken ct)
    {
        string? tenantId = GetEffectiveTenantFilter(httpContext);
        var query = db.ScimUsers
            .AsNoTracking()
            .Include(u => u.GroupMemberships)
            .ThenInclude(m => m.Group)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            query = query.Where(u => u.TenantId == tenantId);
        }

        var users = await query
            .OrderByDescending(u => u.UpdatedAt)
            .Select(u => new
            {
                id = u.Id,
                userName = u.UserName,
                externalId = u.ExternalId,
                displayName = u.FormattedName,
                email = u.Email,
                active = u.Active,
                groups = u.GroupMemberships.Select(g => g.Group != null ? g.Group.DisplayName : g.GroupId).ToList(),
                createdAt = u.CreatedAt,
                updatedAt = u.UpdatedAt
            })
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return TypedResults.Ok(users);
    }

    private static async Task<IResult> GetRevocationsAdminAsync(
        SymbolonDbContext db,
        CancellationToken ct)
    {
        var revs = await db.Revocations
            .OrderByDescending(r => r.Sequence)
            .Take(100)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return TypedResults.Ok(revs);
    }

    private static async Task<IResult> CreateRevocationAdminAsync(
        CreateRevocationDto dto,
        HttpContext context,
        SymbolonDbContext db,
        IAuditLedger audit,
        Webhooks.IWebhookDispatcher webhooks,
        TimeProvider time,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(dto.SubjectType) || string.IsNullOrWhiteSpace(dto.SubjectId))
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status400BadRequest,
                title: "Invalid Revocation Request",
                detail: "SubjectType and SubjectId are required.",
                type: Symbolon.Protocol.ProblemTypes.InvalidRequest);
        }

        string? tenantId = GetEffectiveTenantFilter(context) ?? "default";
        var now = time.GetUtcNow();
        long nextSeq = (await db.Revocations.MaxAsync(r => (long?)r.Sequence, ct).ConfigureAwait(false) ?? 0) + 1;

        var rev = new RevocationEntity
        {
            Id = $"rev_{Guid.NewGuid():N}",
            TenantId = tenantId,
            SubjectType = dto.SubjectType.Trim().ToLowerInvariant(),
            SubjectId = dto.SubjectId.Trim(),
            Reason = dto.Reason?.Trim() ?? "Administrative revocation",
            RevokedAt = now,
            Sequence = nextSeq
        };

        db.Revocations.Add(rev);

        // If license, update state
        if (rev.SubjectType == "license")
        {
            var lic = await db.Licenses.FirstOrDefaultAsync(l => l.Id == rev.SubjectId, ct).ConfigureAwait(false);
            if (lic is not null)
            {
                lic.State = "revoked";
            }
        }
        else if (rev.SubjectType is "kid" or "key")
        {
            var keyEntity = await db.SigningKeys.FirstOrDefaultAsync(k => k.Kid == rev.SubjectId, ct).ConfigureAwait(false);
            if (keyEntity is not null)
            {
                keyEntity.State = "revoked";
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await audit.AppendAsync(new AuditEvent($"{rev.SubjectType}.revoked", rev.SubjectId, null, null, now, rev.Reason), ct).ConfigureAwait(false);
        await webhooks.PublishEventAsync("revocation.created", new
        {
            revocationId = rev.Id,
            subjectType = rev.SubjectType,
            subjectId = rev.SubjectId,
            sequence = rev.Sequence,
            reason = rev.Reason,
            revokedAt = now
        }, tenantId, ct).ConfigureAwait(false);

        return TypedResults.Created($"/admin/v1/revocations/{rev.Id}", rev);
    }

    private static async Task<IResult> GetQueueTicketsAdminAsync(
        string? licenseId,
        string? status,
        int? limit,
        IQueueManager queueManager,
        CancellationToken ct)
    {
        var tickets = await queueManager.GetTicketsAsync(licenseId, status, limit ?? 50, ct).ConfigureAwait(false);
        return TypedResults.Ok(tickets);
    }

    private static async Task<IResult> PromoteQueueTicketAdminAsync(
        string ticket,
        IQueueManager queueManager,
        CancellationToken ct)
    {
        bool success = await queueManager.PromoteTicketAsync(ticket, ct).ConfigureAwait(false);
        if (!success)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Queue Ticket Promotion Failed",
                detail: $"Ticket {ticket} could not be promoted. Either it is not waiting or the license pool has no available capacity.",
                type: Symbolon.Protocol.ProblemTypes.PoolExhausted);
        }

        return TypedResults.Ok(new { status = "promoted", ticket });
    }

    private static async Task<IResult> CancelQueueTicketAdminAsync(
        string ticket,
        IQueueManager queueManager,
        CancellationToken ct)
    {
        bool cancelled = await queueManager.CancelAsync(ticket, ct).ConfigureAwait(false);
        if (!cancelled)
        {
            return Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Queue Ticket Not Found",
                detail: $"Waiting queue ticket '{ticket}' was not found or is already completed.",
                type: Symbolon.Protocol.ProblemTypes.QueueNotFound);
        }

        return TypedResults.NoContent();
    }
}

public sealed record CreateRevocationDto(
    string SubjectType,
    string SubjectId,
    string? Reason = null);
