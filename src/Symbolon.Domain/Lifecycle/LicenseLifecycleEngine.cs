using Symbolon.Domain.Webhooks;

namespace Symbolon.Domain.Lifecycle;

public enum LicenseLifecycleState
{
    ActiveNormal,
    ExpiringSoon,
    SoftGrace,
    HardGrace,
    Expired,
    Perpetual
}

public sealed record LicenseLifecycleInfo(
    string LicenseId,
    string TenantId,
    string ProductCode,
    string CustomerRef,
    DateTimeOffset? ExpiresAt,
    string CurrentState,
    int? SoftGraceDays = null);

public sealed record LicenseEvaluationItem(
    string LicenseId,
    string TenantId,
    string ProductCode,
    string CustomerRef,
    LicenseLifecycleState EvaluatedState,
    TimeSpan? TimeRemaining,
    WebhookEvent? GeneratedEvent,
    string ActionRecommendation);

public sealed record LifecycleEvaluationReport(
    DateTimeOffset EvaluatedAt,
    int TotalEvaluated,
    int PerpetualCount,
    int ActiveNormalCount,
    int ExpiringSoonCount,
    int GraceEnteredCount,
    int ExpiredCount,
    IReadOnlyList<LicenseEvaluationItem> Items,
    IReadOnlyList<WebhookEvent> EventsToDispatch);

public sealed class LicenseLifecycleEngine
{
    public const int DefaultExpiringSoonDays = 14;
    public const int DefaultSoftGraceDays = 7;

    public static LicenseEvaluationItem EvaluateLicense(
        LicenseLifecycleInfo license,
        DateTimeOffset now,
        int expiringSoonDays = DefaultExpiringSoonDays,
        int defaultSoftGraceDays = DefaultSoftGraceDays)
    {
        ArgumentNullException.ThrowIfNull(license);

        if (license.ExpiresAt is null)
        {
            return new LicenseEvaluationItem(
                license.LicenseId,
                license.TenantId,
                license.ProductCode,
                license.CustomerRef,
                LicenseLifecycleState.Perpetual,
                null,
                null,
                "Trvalá perpetual licencia bez expirácie.");
        }

        var expiresAt = license.ExpiresAt.Value;
        var diff = expiresAt - now;
        int graceDays = license.SoftGraceDays ?? defaultSoftGraceDays;
        var graceEnd = expiresAt.AddDays(graceDays);

        if (now > graceEnd)
        {
            var evt = new WebhookEvent(
                $"evt_exp_{Guid.NewGuid():N}",
                WebhookEventTypes.LicenseExpired,
                license.TenantId,
                now,
                new
                {
                    licenseId = license.LicenseId,
                    productCode = license.ProductCode,
                    customerRef = license.CustomerRef,
                    expiredAt = expiresAt,
                    graceEndedAt = graceEnd,
                    daysExpired = (int)(now - expiresAt).TotalDays
                },
                license.LicenseId);

            return new LicenseEvaluationItem(
                license.LicenseId,
                license.TenantId,
                license.ProductCode,
                license.CustomerRef,
                LicenseLifecycleState.Expired,
                diff,
                evt,
                "Platnosť licencie aj ochrannej lehoty vypršala. Prístup k sedadlám je zablokovaný.");
        }

        if (now > expiresAt)
        {
            var daysInGrace = (int)(now - expiresAt).TotalDays;
            var daysRemainingInGrace = graceDays - daysInGrace;

            var evt = new WebhookEvent(
                $"evt_grc_{Guid.NewGuid():N}",
                WebhookEventTypes.LicenseGraceEntered,
                license.TenantId,
                now,
                new
                {
                    licenseId = license.LicenseId,
                    productCode = license.ProductCode,
                    customerRef = license.CustomerRef,
                    expiredAt = expiresAt,
                    daysRemainingInGrace,
                    graceEndsAt = graceEnd
                },
                license.LicenseId);

            return new LicenseEvaluationItem(
                license.LicenseId,
                license.TenantId,
                license.ProductCode,
                license.CustomerRef,
                LicenseLifecycleState.SoftGrace,
                diff,
                evt,
                $"Licencia je v ochrannej lehote (Soft Grace). Zostáva {daysRemainingInGrace} dní do úplného odpojenia.");
        }

        if (diff.TotalDays <= expiringSoonDays)
        {
            int daysRemaining = (int)Math.Ceiling(diff.TotalDays);
            var evt = new WebhookEvent(
                $"evt_soon_{Guid.NewGuid():N}",
                WebhookEventTypes.LicenseExpiringSoon,
                license.TenantId,
                now,
                new
                {
                    licenseId = license.LicenseId,
                    productCode = license.ProductCode,
                    customerRef = license.CustomerRef,
                    expiresAt,
                    daysRemaining
                },
                license.LicenseId);

            return new LicenseEvaluationItem(
                license.LicenseId,
                license.TenantId,
                license.ProductCode,
                license.CustomerRef,
                LicenseLifecycleState.ExpiringSoon,
                diff,
                evt,
                $"Licencia expiruje o {daysRemaining} dní. Odporúča sa kontaktovať zákazníka pre obnovu.");
        }

        return new LicenseEvaluationItem(
            license.LicenseId,
            license.TenantId,
            license.ProductCode,
            license.CustomerRef,
            LicenseLifecycleState.ActiveNormal,
            diff,
            null,
            "Licencia je aktívna.");
    }

    public static LifecycleEvaluationReport EvaluateAll(
        IEnumerable<LicenseLifecycleInfo> licenses,
        DateTimeOffset now,
        int expiringSoonDays = DefaultExpiringSoonDays,
        int softGraceDays = DefaultSoftGraceDays)
    {
        ArgumentNullException.ThrowIfNull(licenses);

        var items = new List<LicenseEvaluationItem>();
        var events = new List<WebhookEvent>();

        int perpetual = 0, active = 0, soon = 0, grace = 0, expired = 0;

        foreach (var lic in licenses)
        {
            var item = EvaluateLicense(lic, now, expiringSoonDays, softGraceDays);
            items.Add(item);

            if (item.GeneratedEvent is not null)
            {
                events.Add(item.GeneratedEvent);
            }

            switch (item.EvaluatedState)
            {
                case LicenseLifecycleState.Perpetual:
                    perpetual++;
                    break;
                case LicenseLifecycleState.ActiveNormal:
                    active++;
                    break;
                case LicenseLifecycleState.ExpiringSoon:
                    soon++;
                    break;
                case LicenseLifecycleState.SoftGrace:
                case LicenseLifecycleState.HardGrace:
                    grace++;
                    break;
                case LicenseLifecycleState.Expired:
                    expired++;
                    break;
            }
        }

        return new LifecycleEvaluationReport(
            now,
            items.Count,
            perpetual,
            active,
            soon,
            grace,
            expired,
            items,
            events);
    }
}
