using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Achilles.Data;
using Achilles.Domain.Lifecycle;
using Achilles.Domain.Webhooks;

namespace Achilles.ControlPlane.Endpoints;

public static class LicenseLifecycleEndpoints
{
    public static RouteGroupBuilder MapLicenseLifecycleEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/v1/lifecycle").WithTags("License Lifecycle");

        group.MapGet("/expiring", GetExpiringLicensesAsync).WithName("GetExpiringLicenses");
        group.MapPost("/evaluate", EvaluateLifecycleAsync).WithName("EvaluateLifecycle");

        return group;
    }

    private static async Task<IResult> GetExpiringLicensesAsync(
        int? days,
        HttpContext context,
        AchillesDbContext db,
        TimeProvider timeProvider,
        LicenseLifecycleEngine engine,
        CancellationToken ct)
    {
        string? tenantId = context.User.IsInRole("admin:super")
            ? context.Request.Headers["X-Tenant-Id"].FirstOrDefault() ?? context.User.FindFirst("tenant_id")?.Value
            : context.User.FindFirst("tenant_id")?.Value;

        int horizonDays = days ?? LicenseLifecycleEngine.DefaultExpiringSoonDays;
        var now = timeProvider.GetUtcNow();
        var maxDate = now.AddDays(horizonDays);

        var query = db.Licenses
            .Include(l => l.Policy)
            .ThenInclude(p => p!.Product)
            .Where(l => l.ExpiresAt != null && l.ExpiresAt <= maxDate);

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            query = query.Where(l => l.TenantId == tenantId);
        }

        var list = await query
            .OrderBy(l => l.ExpiresAt)
            .Select(l => new LicenseLifecycleInfo(
                l.Id,
                l.TenantId,
                l.Policy != null && l.Policy.Product != null ? l.Policy.Product.Code : (l.Policy != null ? l.Policy.Code : "unknown"),
                l.CustomerRef ?? string.Empty,
                l.ExpiresAt,
                l.State,
                7))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var report = LicenseLifecycleEngine.EvaluateAll(list, now, horizonDays);
        return TypedResults.Ok(report);
    }

    private static async Task<IResult> EvaluateLifecycleAsync(
        HttpContext context,
        AchillesDbContext db,
        TimeProvider timeProvider,
        LicenseLifecycleEngine engine,
        IWebhookQueue queue,
        CancellationToken ct)
    {
        string? tenantId = context.User.IsInRole("admin:super")
            ? context.Request.Headers["X-Tenant-Id"].FirstOrDefault() ?? context.User.FindFirst("tenant_id")?.Value
            : context.User.FindFirst("tenant_id")?.Value;

        var now = timeProvider.GetUtcNow();

        var query = db.Licenses
            .Include(l => l.Policy)
            .ThenInclude(p => p!.Product)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            query = query.Where(l => l.TenantId == tenantId);
        }

        var list = await query
            .Select(l => new LicenseLifecycleInfo(
                l.Id,
                l.TenantId,
                l.Policy != null && l.Policy.Product != null ? l.Policy.Product.Code : (l.Policy != null ? l.Policy.Code : "unknown"),
                l.CustomerRef ?? string.Empty,
                l.ExpiresAt,
                l.State,
                7))
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var report = LicenseLifecycleEngine.EvaluateAll(list, now);

        foreach (var evt in report.EventsToDispatch)
        {
            queue.TryEnqueue(evt);
        }

        return TypedResults.Ok(new
        {
            report.EvaluatedAt,
            report.TotalEvaluated,
            EvaluatedLicenses = report.TotalEvaluated,
            report.PerpetualCount,
            report.ActiveNormalCount,
            report.ExpiringSoonCount,
            report.GraceEnteredCount,
            report.ExpiredCount,
            DispatchedEvents = report.EventsToDispatch.Count,
            EventsDispatched = report.EventsToDispatch.Count,
            Items = report.Items.Select(i => new
            {
                i.LicenseId,
                i.TenantId,
                i.ProductCode,
                i.CustomerRef,
                EvaluatedState = i.EvaluatedState.ToString(),
                DaysRemaining = i.TimeRemaining.HasValue ? (int)Math.Ceiling(i.TimeRemaining.Value.TotalDays) : (int?)null,
                i.ActionRecommendation
            })
        });
    }
}
