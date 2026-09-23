using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Symbolon.ControlPlane.Models;
using Symbolon.ControlPlane.Webhooks;
using Symbolon.Data;
using Symbolon.Data.Entities;
using Symbolon.Domain;
using Symbolon.Domain.Entitlements;
using Symbolon.Protocol;

namespace Symbolon.ControlPlane.Endpoints;

public static class EntitlementEndpoints
{
    public static RouteGroupBuilder MapEntitlementEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/v1/entitlements").WithTags("Entitlements");

        group.MapPost("/consume", ConsumeQuotaAsync).WithName("ConsumeQuota");
        group.MapGet("/balance", GetQuotaBalanceAsync).WithName("GetQuotaBalance");

        return group;
    }

    public static RouteGroupBuilder MapAdminEntitlementEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/v1/entitlements")
            .WithTags("Entitlements & Features");

        // Feature Definitions
        group.MapGet("/features", ListFeaturesAsync).WithName("ListFeatureDefinitions");
        group.MapPost("/features", CreateFeatureAsync).WithName("CreateFeatureDefinition");
        group.MapDelete("/features/{id}", DeleteFeatureAsync).WithName("DeleteFeatureDefinition");

        // Package Suites
        group.MapGet("/suites", ListSuitesAsync).WithName("ListPackageSuites");
        group.MapPost("/suites", CreateSuiteAsync).WithName("CreatePackageSuite");
        group.MapDelete("/suites/{id}", DeleteSuiteAsync).WithName("DeletePackageSuite");

        // License Entitlements
        group.MapGet("/licenses/{id}", GetLicenseEntitlementsAsync).WithName("GetLicenseEntitlements");
        group.MapPost("/licenses/{id}", SetLicenseEntitlementAsync).WithName("SetLicenseEntitlement");
        group.MapDelete("/licenses/{id}/{featureCode}", DeleteLicenseEntitlementAsync).WithName("DeleteLicenseEntitlement");

        // Live Usage
        group.MapGet("/usage", GetFeatureUsageAsync).WithName("GetFeatureUsageMetrics");

        return group;
    }

    private static async Task<IResult> ConsumeQuotaAsync(
        ConsumeQuotaRequestDto dto,
        SymbolonDbContext db,
        IAuditLedger audit,
        IWebhookDispatcher webhooks,
        TimeProvider time,
        CancellationToken ct)
    {
        byte[] lookup = SHA256.HashData(Encoding.UTF8.GetBytes(dto.LicenseKey.Trim()))[..4];

        var license = await db.Licenses
            .Include(l => l.Quotas)
            .FirstOrDefaultAsync(l => l.KeyLookup == lookup, ct)
            .ConfigureAwait(false);

        if (license is null || license.State != "active")
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "License Not Found",
                type: ProblemTypes.LicenseNotFound);
        }

        var now = time.GetUtcNow();
        if (license.ExpiresAt.HasValue && license.ExpiresAt.Value < now)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status403Forbidden,
                title: "License Expired",
                type: ProblemTypes.LicenseNotFound);
        }

        var quota = license.Quotas.FirstOrDefault(q =>
            string.Equals(q.EntitlementCode, dto.EntitlementCode.Trim(), StringComparison.OrdinalIgnoreCase));

        if (quota is null)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Entitlement Quota Not Found",
                detail: $"Entitlement '{dto.EntitlementCode}' is not configured for quota tracking on this license.",
                type: ProblemTypes.InvalidRequest);
        }

        long remaining = quota.TotalUnits - quota.ConsumedUnits;
        if (remaining < dto.Units)
        {
            return TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Quota Limit Exhausted",
                detail: $"Requested {dto.Units} units, but only {remaining} units remain available.",
                type: ProblemTypes.QuotaExhausted);
        }

        quota.ConsumedUnits += dto.Units;
        quota.UpdatedAt = now;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        long newRemaining = quota.TotalUnits - quota.ConsumedUnits;

        await audit.AppendAsync(new AuditEvent(
            "quota.consumed",
            license.Id,
            null,
            null,
            now,
            $"Consumed {dto.Units} units of {quota.EntitlementCode}. Remaining: {newRemaining}"), ct).ConfigureAwait(false);

        await webhooks.PublishEventAsync("quota.consumed", new
        {
            licenseId = license.Id,
            tenantId = license.TenantId,
            customerRef = license.CustomerRef,
            entitlementCode = quota.EntitlementCode,
            consumedUnits = dto.Units,
            remainingUnits = newRemaining
        }, license.TenantId, ct).ConfigureAwait(false);

        return TypedResults.Ok(new QuotaBalanceDto(
            quota.EntitlementCode,
            quota.TotalUnits,
            quota.ConsumedUnits,
            newRemaining,
            quota.UpdatedAt));
    }

    private static async Task<IResult> GetQuotaBalanceAsync(
        string licenseKey,
        SymbolonDbContext db,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(licenseKey))
        {
            return TypedResults.BadRequest("License key parameter is required.");
        }

        byte[] lookup = SHA256.HashData(Encoding.UTF8.GetBytes(licenseKey.Trim()))[..4];

        var license = await db.Licenses
            .Include(l => l.Quotas)
            .FirstOrDefaultAsync(l => l.KeyLookup == lookup, ct)
            .ConfigureAwait(false);

        if (license is null || license.State != "active")
        {
            return TypedResults.NotFound();
        }

        var balances = license.Quotas.Select(q => new QuotaBalanceDto(
            q.EntitlementCode,
            q.TotalUnits,
            q.ConsumedUnits,
            q.TotalUnits - q.ConsumedUnits,
            q.UpdatedAt)).ToList();

        return TypedResults.Ok(balances);
    }

    private static async Task<IResult> ListFeaturesAsync(
        string? productId,
        HttpContext context,
        IFeatureEntitlementStore store,
        CancellationToken ct)
    {
        string? tenantId = ResolveTenantId(context);
        var list = await store.ListFeatureDefinitionsAsync(tenantId, productId, ct).ConfigureAwait(false);
        return TypedResults.Ok(list);
    }

    private static async Task<IResult> CreateFeatureAsync(
        CreateFeatureDto dto,
        HttpContext context,
        IFeatureEntitlementStore store,
        CancellationToken ct)
    {
        string tenantId = ResolveTenantId(context) ?? "default";

        var model = new FeatureDefinitionModel(
            Id: Guid.NewGuid().ToString("N"),
            TenantId: tenantId,
            ProductId: dto.ProductId,
            Code: dto.Code,
            Name: dto.Name,
            Description: dto.Description,
            MinVersion: dto.MinVersion,
            MaxVersion: dto.MaxVersion,
            IsFloating: dto.IsFloating,
            DefaultMaxSeats: dto.DefaultMaxSeats,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

        var created = await store.CreateFeatureDefinitionAsync(model, ct).ConfigureAwait(false);
        return TypedResults.Created($"/admin/v1/entitlements/features/{created.Id}", created);
    }

    private static async Task<IResult> DeleteFeatureAsync(
        string id,
        HttpContext context,
        IFeatureEntitlementStore store,
        CancellationToken ct)
    {
        string? tenantId = ResolveTenantId(context);
        bool deleted = await store.DeleteFeatureDefinitionAsync(id, tenantId, ct).ConfigureAwait(false);
        return deleted ? TypedResults.NoContent() : TypedResults.NotFound();
    }

    private static async Task<IResult> ListSuitesAsync(
        string? productId,
        HttpContext context,
        IFeatureEntitlementStore store,
        CancellationToken ct)
    {
        string? tenantId = ResolveTenantId(context);
        var list = await store.ListPackageSuitesAsync(tenantId, productId, ct).ConfigureAwait(false);
        return TypedResults.Ok(list);
    }

    private static async Task<IResult> CreateSuiteAsync(
        CreatePackageSuiteDto dto,
        HttpContext context,
        IFeatureEntitlementStore store,
        CancellationToken ct)
    {
        string tenantId = ResolveTenantId(context) ?? "default";

        var model = new PackageSuiteModel(
            Id: Guid.NewGuid().ToString("N"),
            TenantId: tenantId,
            ProductId: dto.ProductId,
            Code: dto.Code,
            Name: dto.Name,
            Description: dto.Description,
            FeatureCodes: dto.FeatureCodes,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

        var created = await store.CreatePackageSuiteAsync(model, ct).ConfigureAwait(false);
        return TypedResults.Created($"/admin/v1/entitlements/suites/{created.Id}", created);
    }

    private static async Task<IResult> DeleteSuiteAsync(
        string id,
        HttpContext context,
        IFeatureEntitlementStore store,
        CancellationToken ct)
    {
        string? tenantId = ResolveTenantId(context);
        bool deleted = await store.DeletePackageSuiteAsync(id, tenantId, ct).ConfigureAwait(false);
        return deleted ? TypedResults.NoContent() : TypedResults.NotFound();
    }

    private static async Task<IResult> GetLicenseEntitlementsAsync(
        string id,
        IFeatureEntitlementStore store,
        CancellationToken ct)
    {
        var list = await store.GetEntitlementsForLicenseAsync(id, ct).ConfigureAwait(false);
        return TypedResults.Ok(list);
    }

    private static async Task<IResult> SetLicenseEntitlementAsync(
        string id,
        SetLicenseEntitlementDto dto,
        HttpContext context,
        IFeatureEntitlementStore store,
        CancellationToken ct)
    {
        string tenantId = ResolveTenantId(context) ?? "default";

        var model = new LicenseEntitlementModel(
            Id: Guid.NewGuid().ToString("N"),
            TenantId: tenantId,
            LicenseId: id,
            FeatureCode: dto.FeatureCode,
            MaxSeats: dto.MaxSeats,
            AllowedVersionRange: dto.AllowedVersionRange,
            IsEnabled: dto.IsEnabled,
            ParametersJson: dto.ParametersJson,
            CreatedAt: DateTimeOffset.UtcNow,
            UpdatedAt: DateTimeOffset.UtcNow);

        var saved = await store.SetLicenseEntitlementAsync(model, ct).ConfigureAwait(false);
        return TypedResults.Ok(saved);
    }

    private static async Task<IResult> DeleteLicenseEntitlementAsync(
        string id,
        string featureCode,
        IFeatureEntitlementStore store,
        CancellationToken ct)
    {
        bool deleted = await store.DeleteLicenseEntitlementAsync(id, featureCode, ct).ConfigureAwait(false);
        return deleted ? TypedResults.NoContent() : TypedResults.NotFound();
    }

    private static async Task<IResult> GetFeatureUsageAsync(
        HttpContext context,
        IFeatureEntitlementStore store,
        TimeProvider timeProvider,
        CancellationToken ct)
    {
        string? tenantId = ResolveTenantId(context);
        var now = timeProvider.GetUtcNow();
        var usage = await store.GetFeatureUsageMetricsAsync(tenantId, now, ct).ConfigureAwait(false);
        return TypedResults.Ok(usage);
    }

    private static string? ResolveTenantId(HttpContext context)
    {
        if (context.User.IsInRole("admin:super"))
        {
            return context.Request.Headers["X-Tenant-Id"].FirstOrDefault() ??
                   context.User.FindFirst("tenant_id")?.Value;
        }

        return context.User.FindFirst("tenant_id")?.Value;
    }
}
