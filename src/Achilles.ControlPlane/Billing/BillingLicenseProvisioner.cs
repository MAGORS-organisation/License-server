using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Achilles.Data;
using Achilles.Data.Entities;
using Achilles.Domain;
using Achilles.Domain.Billing;
using Achilles.Format;

namespace Achilles.ControlPlane.Billing;

public sealed class BillingLicenseProvisioner
{
    private readonly AchillesDbContext _db;
    private readonly IAuditLedger _audit;
    private readonly TimeProvider _time;

    public BillingLicenseProvisioner(AchillesDbContext db, IAuditLedger audit, TimeProvider time)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(audit);
        ArgumentNullException.ThrowIfNull(time);

        _db = db;
        _audit = audit;
        _time = time;
    }

    public async Task<BillingProcessResult> ProcessAsync(BillingWebhookEvent evt, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(evt);

        var now = _time.GetUtcNow();

        return evt.Action switch
        {
            BillingAction.Provision => await HandleProvisionAsync(evt, now, ct).ConfigureAwait(false),
            BillingAction.Renew => await HandleRenewAsync(evt, now, ct).ConfigureAwait(false),
            BillingAction.UpdateSeats => await HandleUpdateSeatsAsync(evt, now, ct).ConfigureAwait(false),
            BillingAction.Suspend => await HandleSuspendAsync(evt, now, ct).ConfigureAwait(false),
            BillingAction.Revoke => await HandleRevokeAsync(evt, now, ct).ConfigureAwait(false),
            _ => new BillingProcessResult(true, "ignored", null, null, $"Ignored billing event '{evt.EventType}'.", evt.CustomerId, evt.SubscriptionId)
        };
    }

    private async Task<BillingProcessResult> HandleProvisionAsync(BillingWebhookEvent evt, DateTimeOffset now, CancellationToken ct)
    {
        // Prevent duplicate provisioning if subscription already exists
        if (!string.IsNullOrWhiteSpace(evt.SubscriptionId))
        {
            var existing = await FindLicenseByRefAsync(evt.SubscriptionId, null, ct).ConfigureAwait(false);
            if (existing is not null)
            {
                return await HandleRenewAsync(evt, now, ct).ConfigureAwait(false);
            }
        }

        // 1. Resolve Tenant
        var tenant = await _db.Tenants.FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (tenant is null)
        {
            tenant = new Tenant
            {
                Id = "ten_default",
                Slug = "default",
                Name = "Default Organization",
                CreatedAt = now
            };
            _db.Tenants.Add(tenant);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        // 2. Resolve Policy
        Policy? policy = null;
        if (evt.Metadata.TryGetValue("policyId", out var pid))
        {
            policy = await _db.Policies.FirstOrDefaultAsync(p => p.Id == pid, ct).ConfigureAwait(false);
        }
        else if (evt.Metadata.TryGetValue("policyCode", out var pcode))
        {
            policy = await _db.Policies.FirstOrDefaultAsync(p => p.Code == pcode, ct).ConfigureAwait(false);
        }

        if (policy is null && !string.IsNullOrWhiteSpace(evt.ProductCode))
        {
            policy = await _db.Policies
                .Include(p => p.Product)
                .FirstOrDefaultAsync(p => p.Product != null && p.Product.Code == evt.ProductCode, ct)
                .ConfigureAwait(false);
        }

        if (policy is null)
        {
            policy = await _db.Policies.FirstOrDefaultAsync(p => p.TenantId == tenant.Id, ct).ConfigureAwait(false);
        }

        if (policy is null)
        {
            // Create default policy for new purchases
            var product = await _db.Products.FirstOrDefaultAsync(p => p.TenantId == tenant.Id, ct).ConfigureAwait(false);
            if (product is null)
            {
                product = new Product
                {
                    Id = $"prod_{Guid.NewGuid():N}",
                    TenantId = tenant.Id,
                    Code = evt.ProductCode ?? "standard-suite",
                    Name = "Standard Suite",
                    PlatformsJson = "[\"windows\",\"linux\",\"darwin\"]",
                    CreatedAt = now
                };
                _db.Products.Add(product);
                await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            policy = new Policy
            {
                Id = $"pol_{Guid.NewGuid():N}",
                TenantId = tenant.Id,
                ProductId = product.Id,
                Code = evt.PlanCode ?? "subscription-floating",
                Name = "Subscription Floating Policy",
                LicenseModel = "floating",
                MaxSeats = Math.Max(1, evt.Seats)
            };
            _db.Policies.Add(policy);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        // 3. Generate key and hashes
        string rawKey = LicenseKey.Generate("SYM").Canonical;
        byte[] lookup = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey))[..4];
        string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey)));

        string licenseId = $"lic_{Guid.NewGuid():N}";
        int seats = Math.Max(1, evt.Seats);
        var expiresAt = evt.ExpiresAt ?? now.AddYears(1);

        var license = new LicenseEntity
        {
            Id = licenseId,
            TenantId = policy.TenantId,
            PolicyId = policy.Id,
            KeyLookup = lookup,
            KeyHash = hash,
            CustomerRef = evt.SubscriptionId ?? evt.CustomerId,
            State = "active",
            MaxSeats = seats,
            IssuedAt = now,
            ExpiresAt = expiresAt,
            CreatedAt = now
        };

        _db.Licenses.Add(license);

        for (int i = 1; i <= seats; i++)
        {
            _db.Seats.Add(new SeatEntity
            {
                LicenseId = licenseId,
                SeatNo = i,
                IsOverage = false
            });
        }

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.AppendAsync(new AuditEvent(
            "billing.license_provisioned",
            licenseId,
            null,
            null,
            now,
            $"Provisioned {seats} seats via {evt.Provider} ({evt.EventType}) for {evt.CustomerEmail}"), ct).ConfigureAwait(false);

        return new BillingProcessResult(
            true,
            "provisioned",
            licenseId,
            rawKey,
            $"License successfully provisioned with {seats} seats.",
            evt.CustomerId,
            evt.SubscriptionId);
    }

    private async Task<BillingProcessResult> HandleRenewAsync(BillingWebhookEvent evt, DateTimeOffset now, CancellationToken ct)
    {
        var license = await FindLicenseByRefAsync(evt.SubscriptionId, evt.CustomerId, ct).ConfigureAwait(false);
        if (license is null)
        {
            return new BillingProcessResult(
                false,
                "not_found",
                null,
                null,
                $"No license found matching reference '{evt.SubscriptionId ?? evt.CustomerId}'.",
                evt.CustomerId,
                evt.SubscriptionId);
        }

        var newExpiration = evt.ExpiresAt ?? now.AddYears(1);
        license.ExpiresAt = newExpiration;
        license.State = "active";

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.AppendAsync(new AuditEvent(
            "billing.license_renewed",
            license.Id,
            null,
            null,
            now,
            $"Renewed license until {newExpiration} via {evt.Provider}"), ct).ConfigureAwait(false);

        return new BillingProcessResult(
            true,
            "renewed",
            license.Id,
            null,
            $"License renewed until {newExpiration}.",
            evt.CustomerId,
            evt.SubscriptionId);
    }

    private async Task<BillingProcessResult> HandleUpdateSeatsAsync(BillingWebhookEvent evt, DateTimeOffset now, CancellationToken ct)
    {
        var license = await FindLicenseByRefAsync(evt.SubscriptionId, evt.CustomerId, ct).ConfigureAwait(false);
        if (license is null)
        {
            return new BillingProcessResult(
                false,
                "not_found",
                null,
                null,
                $"No license found matching reference '{evt.SubscriptionId ?? evt.CustomerId}'.",
                evt.CustomerId,
                evt.SubscriptionId);
        }

        int oldSeats = license.MaxSeats;
        int newSeats = Math.Max(1, evt.Seats);

        if (newSeats > oldSeats)
        {
            for (int i = oldSeats + 1; i <= newSeats; i++)
            {
                _db.Seats.Add(new SeatEntity
                {
                    LicenseId = license.Id,
                    SeatNo = i,
                    IsOverage = false
                });
            }
        }

        license.MaxSeats = newSeats;
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.AppendAsync(new AuditEvent(
            "billing.seats_updated",
            license.Id,
            null,
            null,
            now,
            $"Seats updated from {oldSeats} to {newSeats} via {evt.Provider}"), ct).ConfigureAwait(false);

        return new BillingProcessResult(
            true,
            "updated_seats",
            license.Id,
            null,
            $"Seats successfully updated from {oldSeats} to {newSeats}.",
            evt.CustomerId,
            evt.SubscriptionId);
    }

    private async Task<BillingProcessResult> HandleSuspendAsync(BillingWebhookEvent evt, DateTimeOffset now, CancellationToken ct)
    {
        var license = await FindLicenseByRefAsync(evt.SubscriptionId, evt.CustomerId, ct).ConfigureAwait(false);
        if (license is null)
        {
            return new BillingProcessResult(
                false,
                "not_found",
                null,
                null,
                $"No license found matching reference '{evt.SubscriptionId ?? evt.CustomerId}'.",
                evt.CustomerId,
                evt.SubscriptionId);
        }

        license.State = "suspended";
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.AppendAsync(new AuditEvent(
            "billing.license_suspended",
            license.Id,
            null,
            null,
            now,
            $"Suspended license via {evt.Provider} ({evt.EventType})"), ct).ConfigureAwait(false);

        return new BillingProcessResult(
            true,
            "suspended",
            license.Id,
            null,
            "License suspended due to billing event.",
            evt.CustomerId,
            evt.SubscriptionId);
    }

    private async Task<BillingProcessResult> HandleRevokeAsync(BillingWebhookEvent evt, DateTimeOffset now, CancellationToken ct)
    {
        var license = await FindLicenseByRefAsync(evt.SubscriptionId, evt.CustomerId, ct).ConfigureAwait(false);
        if (license is null)
        {
            return new BillingProcessResult(
                false,
                "not_found",
                null,
                null,
                $"No license found matching reference '{evt.SubscriptionId ?? evt.CustomerId}'.",
                evt.CustomerId,
                evt.SubscriptionId);
        }

        license.State = "revoked";
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        await _audit.AppendAsync(new AuditEvent(
            "billing.license_revoked",
            license.Id,
            null,
            null,
            now,
            $"Revoked license via {evt.Provider} ({evt.EventType})"), ct).ConfigureAwait(false);

        return new BillingProcessResult(
            true,
            "revoked",
            license.Id,
            null,
            "License revoked due to billing cancellation/refund.",
            evt.CustomerId,
            evt.SubscriptionId);
    }

    private Task<LicenseEntity?> FindLicenseByRefAsync(string? subId, string? customerId, CancellationToken ct)
    {
        return _db.Licenses
            .OrderByDescending(l => l.CreatedAt)
            .FirstOrDefaultAsync(l =>
                (!string.IsNullOrEmpty(subId) && l.CustomerRef == subId) ||
                (!string.IsNullOrEmpty(customerId) && l.CustomerRef == customerId), ct);
    }
}
