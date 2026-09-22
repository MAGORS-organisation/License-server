using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Symbolon.Data;
using Symbolon.Data.Entities;
using Symbolon.Domain;
using Symbolon.Domain.Migration;
using Symbolon.Format;

namespace Symbolon.ControlPlane.Endpoints;

public sealed record FlexNetLicenseMigrateRequest(
    string Content,
    string? TenantId = null,
    bool Apply = false);

public sealed record FlexNetOptionsMigrateRequest(
    string Content,
    string? PolicyId = null,
    bool Apply = false);

public sealed record FlexNetLogAnalysisRequest(
    string Content,
    string? FeatureFilter = null);

public sealed record KeygenImportRequest(
    string Content,
    string? TenantId = null,
    bool Apply = false);

public sealed record ImportedLicenseItem(
    string ProductCode,
    string PolicyCode,
    string LicenseId,
    string LicenseKey,
    int Seats,
    bool IsPermanent);

public static class MigrationEndpoints
{
    public static void MapMigrationEndpoints(this IEndpointRouteBuilder app)
    {
        ArgumentNullException.ThrowIfNull(app);

        var group = app.MapGroup("/admin/v1/migrate")
            .WithTags("Migration (FlexNet & Keygen)");

        // 1. FlexNet license.dat Migration & Conversion
        group.MapPost("/flexnet/license", async (
            FlexNetLicenseMigrateRequest req,
            SymbolonDbContext db,
            TimeProvider time,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Content))
            {
                return Results.BadRequest(new { error = "Content is required." });
            }

            var parsed = FlexNetLicenseParser.Parse(req.Content);
            var converted = FlexNetLicenseParser.ConvertToSymbolonPlans(parsed);

            var importedItems = new List<ImportedLicenseItem>();

            if (req.Apply)
            {
                var now = time.GetUtcNow();
                string tenantId = req.TenantId ?? (await db.Tenants.Select(t => t.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false)) ?? "ten_default";

                // Ensure tenant exists
                var tenant = await db.Tenants.FindAsync([tenantId], ct).ConfigureAwait(false);
                if (tenant is null)
                {
                    tenant = new Tenant
                    {
                        Id = tenantId,
                        Slug = "migrated-tenant",
                        Name = "Migrated Tenant",
                        CreatedAt = now
                    };
                    db.Tenants.Add(tenant);
                }

                foreach (var plan in converted.ConvertedPlans)
                {
                    // 1. Find or create product
                    var product = await db.Products.FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Code == plan.Code, ct).ConfigureAwait(false);
                    if (product is null)
                    {
                        product = new Product
                        {
                            Id = $"prod_{Guid.NewGuid():N}",
                            TenantId = tenantId,
                            Code = plan.Code,
                            Name = plan.Name,
                            CreatedAt = now
                        };
                        db.Products.Add(product);
                    }

                    // 2. Find or create policy
                    string policyCode = $"{plan.Code}_MIGRATED";
                    var policy = await db.Policies.FirstOrDefaultAsync(p => p.TenantId == tenantId && p.ProductId == product.Id && p.Code == policyCode, ct).ConfigureAwait(false);
                    if (policy is null)
                    {
                        policy = new Policy
                        {
                            Id = $"pol_{Guid.NewGuid():N}",
                            TenantId = tenantId,
                            ProductId = product.Id,
                            Code = policyCode,
                            Name = $"{plan.Name} Policy",
                            LicenseModel = plan.NodeLockHostId != null ? "node-lock" : "floating",
                            MaxSeats = plan.TotalSeats,
                            EntitlementsJson = JsonSerializer.Serialize(plan.Components)
                        };
                        db.Policies.Add(policy);
                    }

                    // 3. Issue license
                    string rawKey = LicenseKey.Generate("SYM").Canonical;
                    byte[] lookup = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey))[..4];
                    string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey)));

                    string licenseId = $"lic_{Guid.NewGuid():N}";
                    var expDate = plan.IsPermanent ? now.AddYears(10) : (plan.ExpiresAt ?? now.AddYears(1));

                    var lic = new LicenseEntity
                    {
                        Id = licenseId,
                        TenantId = tenantId,
                        PolicyId = policy.Id,
                        KeyLookup = lookup,
                        KeyHash = hash,
                        State = "active",
                        MaxSeats = plan.TotalSeats,
                        IssuedAt = now,
                        ExpiresAt = expDate,
                        CreatedAt = now
                    };
                    db.Licenses.Add(lic);

                    // Materialize seats
                    int seatsToCreate = Math.Min(plan.TotalSeats, 100); // capped at 100 for batch import performance
                    for (int i = 1; i <= seatsToCreate; i++)
                    {
                        db.Seats.Add(new SeatEntity
                        {
                            LicenseId = licenseId,
                            SeatNo = i,
                            IsOverage = false
                        });
                    }

                    importedItems.Add(new ImportedLicenseItem(
                        ProductCode: plan.Code,
                        PolicyCode: policy.Code,
                        LicenseId: licenseId,
                        LicenseKey: rawKey,
                        Seats: plan.TotalSeats,
                        IsPermanent: plan.IsPermanent));
                }

                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            return Results.Ok(new
            {
                parsed = converted.ParsedFile,
                plans = converted.ConvertedPlans,
                recommendations = converted.Recommendations,
                applied = req.Apply,
                importedCount = importedItems.Count,
                importedLicenses = importedItems
            });
        });

        // 2. FlexNet options.opt Transpiler
        group.MapPost("/flexnet/options", async (
            FlexNetOptionsMigrateRequest req,
            SymbolonDbContext db,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Content))
            {
                return Results.BadRequest(new { error = "Content is required." });
            }

            var report = FlexNetOptionsTranspiler.Transpile(req.Content);

            if (req.Apply && !string.IsNullOrWhiteSpace(req.PolicyId))
            {
                var policy = await db.Policies.FindAsync([req.PolicyId], ct).ConfigureAwait(false);
                if (policy is not null)
                {
                    policy.BorrowEnabled = report.Policy.Borrowing.Enabled;
                    policy.BorrowMaxDurationDays = report.Policy.Borrowing.MaxBorrowDurationDays;
                    if (report.Policy.IdleTimeoutSeconds.HasValue)
                    {
                        policy.LeaseTtlSeconds = report.Policy.IdleTimeoutSeconds.Value;
                    }
                    await db.SaveChangesAsync(ct).ConfigureAwait(false);
                }
            }

            return Results.Ok(report);
        });

        // 3. FlexNet lmgrd.log Concurrency & Right-Sizing Analysis
        group.MapPost("/flexnet/log-analysis", (FlexNetLogAnalysisRequest req) =>
        {
            if (string.IsNullOrWhiteSpace(req.Content))
            {
                return Results.BadRequest(new { error = "Content is required." });
            }

            var result = FlexNetLogAnalyzer.Analyze(req.Content);
            return Results.Ok(result);
        });

        // 4. Keygen.sh JSON Import
        group.MapPost("/keygen", async (
            KeygenImportRequest req,
            SymbolonDbContext db,
            TimeProvider time,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(req.Content))
            {
                return Results.BadRequest(new { error = "Content is required." });
            }

            var summary = KeygenImporter.Parse(req.Content);
            var importedLicenses = new List<ImportedLicenseItem>();

            if (req.Apply)
            {
                var now = time.GetUtcNow();
                string tenantId = req.TenantId ?? (await db.Tenants.Select(t => t.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false)) ?? "ten_default";

                // Ensure tenant exists
                var tenant = await db.Tenants.FindAsync([tenantId], ct).ConfigureAwait(false);
                if (tenant is null)
                {
                    tenant = new Tenant
                    {
                        Id = tenantId,
                        Slug = "keygen-imported-tenant",
                        Name = "Keygen Imported Tenant",
                        CreatedAt = now
                    };
                    db.Tenants.Add(tenant);
                }

                // Create a default product for Keygen imports if needed
                var defaultProd = await db.Products.FirstOrDefaultAsync(p => p.TenantId == tenantId, ct).ConfigureAwait(false);
                if (defaultProd is null)
                {
                    defaultProd = new Product
                    {
                        Id = $"prod_{Guid.NewGuid():N}",
                        TenantId = tenantId,
                        Code = "KEYGEN_PRODUCT",
                        Name = "Keygen Imported Product",
                        CreatedAt = now
                    };
                    db.Products.Add(defaultProd);
                }

                var policyMap = new Dictionary<string, Policy>(StringComparer.OrdinalIgnoreCase);

                foreach (var kp in summary.Policies)
                {
                    var pol = new Policy
                    {
                        Id = $"pol_{Guid.NewGuid():N}",
                        TenantId = tenantId,
                        ProductId = defaultProd.Id,
                        Code = kp.Code,
                        Name = kp.Name,
                        LicenseModel = kp.LicenseModel,
                        MaxSeats = kp.MaxSeats,
                        Duration = kp.DurationDays > 0 ? $"{kp.DurationDays}d" : "365d"
                    };
                    db.Policies.Add(pol);
                    policyMap[kp.Id] = pol;
                }

                foreach (var kl in summary.Licenses)
                {
                    string polId = policyMap.TryGetValue(kl.PolicyId, out var pObj) ? pObj.Id : (await db.Policies.Select(p => p.Id).FirstOrDefaultAsync(ct).ConfigureAwait(false) ?? "pol_default");

                    string rawKey = kl.Key;
                    byte[] lookup = SHA256.HashData(Encoding.UTF8.GetBytes(rawKey))[..4];
                    string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(rawKey)));

                    string licId = $"lic_{Guid.NewGuid():N}";
                    var lic = new LicenseEntity
                    {
                        Id = licId,
                        TenantId = tenantId,
                        PolicyId = polId,
                        KeyLookup = lookup,
                        KeyHash = hash,
                        CustomerRef = kl.Name,
                        State = string.Equals(kl.Status, "ACTIVE", StringComparison.OrdinalIgnoreCase) ? "active" : "revoked",
                        MaxSeats = kl.MaxSeats,
                        IssuedAt = now,
                        ExpiresAt = kl.ExpiresAt ?? now.AddYears(1),
                        CreatedAt = now
                    };
                    db.Licenses.Add(lic);

                    for (int i = 1; i <= Math.Min(kl.MaxSeats, 100); i++)
                    {
                        db.Seats.Add(new SeatEntity
                        {
                            LicenseId = licId,
                            SeatNo = i,
                            IsOverage = false
                        });
                    }

                    importedLicenses.Add(new ImportedLicenseItem(
                        ProductCode: defaultProd.Code,
                        PolicyCode: polId,
                        LicenseId: licId,
                        LicenseKey: rawKey,
                        Seats: kl.MaxSeats,
                        IsPermanent: false));
                }

                await db.SaveChangesAsync(ct).ConfigureAwait(false);
            }

            return Results.Ok(new
            {
                summary,
                applied = req.Apply,
                importedCount = importedLicenses.Count,
                importedLicenses
            });
        });
    }
}
