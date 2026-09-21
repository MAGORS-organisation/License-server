using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Symbolon.ControlPlane.Webhooks;
using Symbolon.Data;
using Symbolon.Data.Entities;
using Symbolon.Domain;
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
}
