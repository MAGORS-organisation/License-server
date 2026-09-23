using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Symbolon.Data.Entities;
using Symbolon.Domain.Entitlements;

namespace Symbolon.Data.Stores;

public sealed class EfFeatureEntitlementStore : IFeatureEntitlementStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly SymbolonDbContext _db;
    private readonly TimeProvider _timeProvider;

    public EfFeatureEntitlementStore(SymbolonDbContext db, TimeProvider timeProvider)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    // --- Feature Definitions ---

    public async Task<IReadOnlyList<FeatureDefinitionModel>> ListFeatureDefinitionsAsync(
        string? tenantId,
        string? productId = null,
        CancellationToken ct = default)
    {
        var query = _db.FeatureDefinitions.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            query = query.Where(f => f.TenantId == tenantId);
        }

        if (!string.IsNullOrWhiteSpace(productId))
        {
            query = query.Where(f => f.ProductId == productId);
        }

        var entities = await query.OrderBy(f => f.Code).ToListAsync(ct).ConfigureAwait(false);

        return entities.Select(MapFeatureToModel).ToList();
    }

    public async Task<FeatureDefinitionModel?> GetFeatureDefinitionByCodeAsync(
        string tenantId,
        string code,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var entity = await _db.FeatureDefinitions.AsNoTracking()
            .FirstOrDefaultAsync(f => f.TenantId == tenantId && f.Code == code, ct)
            .ConfigureAwait(false);

        return entity is null ? null : MapFeatureToModel(entity);
    }

    public async Task<FeatureDefinitionModel> CreateFeatureDefinitionAsync(
        FeatureDefinitionModel model,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        var now = _timeProvider.GetUtcNow();
        var entity = new FeatureDefinitionEntity
        {
            Id = string.IsNullOrWhiteSpace(model.Id) ? Guid.NewGuid().ToString("N") : model.Id,
            TenantId = model.TenantId,
            ProductId = model.ProductId,
            Code = model.Code.Trim().ToUpperInvariant(),
            Name = model.Name,
            Description = model.Description,
            MinVersion = model.MinVersion,
            MaxVersion = model.MaxVersion,
            IsFloating = model.IsFloating,
            DefaultMaxSeats = model.DefaultMaxSeats,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.FeatureDefinitions.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return MapFeatureToModel(entity);
    }

    public async Task<bool> DeleteFeatureDefinitionAsync(
        string id,
        string? tenantId = null,
        CancellationToken ct = default)
    {
        var query = _db.FeatureDefinitions.Where(f => f.Id == id);
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            query = query.Where(f => f.TenantId == tenantId);
        }

        var entity = await query.FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (entity is null) return false;

        _db.FeatureDefinitions.Remove(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    // --- Package Suites ---

    public async Task<IReadOnlyList<PackageSuiteModel>> ListPackageSuitesAsync(
        string? tenantId,
        string? productId = null,
        CancellationToken ct = default)
    {
        var query = _db.PackageSuites.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            query = query.Where(s => s.TenantId == tenantId);
        }

        if (!string.IsNullOrWhiteSpace(productId))
        {
            query = query.Where(s => s.ProductId == productId);
        }

        var entities = await query.OrderBy(s => s.Code).ToListAsync(ct).ConfigureAwait(false);

        return entities.Select(MapSuiteToModel).ToList();
    }

    public async Task<PackageSuiteModel?> GetPackageSuiteByCodeAsync(
        string tenantId,
        string code,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);

        var entity = await _db.PackageSuites.AsNoTracking()
            .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Code == code, ct)
            .ConfigureAwait(false);

        return entity is null ? null : MapSuiteToModel(entity);
    }

    public async Task<PackageSuiteModel> CreatePackageSuiteAsync(
        PackageSuiteModel model,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        var now = _timeProvider.GetUtcNow();
        var entity = new PackageSuiteEntity
        {
            Id = string.IsNullOrWhiteSpace(model.Id) ? Guid.NewGuid().ToString("N") : model.Id,
            TenantId = model.TenantId,
            ProductId = model.ProductId,
            Code = model.Code.Trim().ToUpperInvariant(),
            Name = model.Name,
            Description = model.Description,
            FeatureCodesJson = JsonSerializer.Serialize(model.FeatureCodes, JsonOptions),
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.PackageSuites.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return MapSuiteToModel(entity);
    }

    public async Task<bool> DeletePackageSuiteAsync(
        string id,
        string? tenantId = null,
        CancellationToken ct = default)
    {
        var query = _db.PackageSuites.Where(s => s.Id == id);
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            query = query.Where(s => s.TenantId == tenantId);
        }

        var entity = await query.FirstOrDefaultAsync(ct).ConfigureAwait(false);
        if (entity is null) return false;

        _db.PackageSuites.Remove(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    // --- License Entitlements ---

    public async Task<IReadOnlyList<LicenseEntitlementModel>> GetEntitlementsForLicenseAsync(
        string licenseId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(licenseId);

        var entities = await _db.LicenseEntitlements.AsNoTracking()
            .Where(e => e.LicenseId == licenseId)
            .OrderBy(e => e.FeatureCode)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return entities.Select(MapEntitlementToModel).ToList();
    }

    public async Task<LicenseEntitlementModel> SetLicenseEntitlementAsync(
        LicenseEntitlementModel model,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(model);

        var now = _timeProvider.GetUtcNow();
        var existing = await _db.LicenseEntitlements
            .FirstOrDefaultAsync(e => e.LicenseId == model.LicenseId && e.FeatureCode == model.FeatureCode, ct)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            existing.MaxSeats = model.MaxSeats;
            existing.AllowedVersionRange = model.AllowedVersionRange;
            existing.IsEnabled = model.IsEnabled;
            existing.ParametersJson = model.ParametersJson;
            existing.UpdatedAt = now;
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return MapEntitlementToModel(existing);
        }

        var entity = new LicenseEntitlementEntity
        {
            Id = string.IsNullOrWhiteSpace(model.Id) ? Guid.NewGuid().ToString("N") : model.Id,
            TenantId = model.TenantId,
            LicenseId = model.LicenseId,
            FeatureCode = model.FeatureCode.Trim().ToUpperInvariant(),
            MaxSeats = model.MaxSeats,
            AllowedVersionRange = model.AllowedVersionRange,
            IsEnabled = model.IsEnabled,
            ParametersJson = model.ParametersJson,
            CreatedAt = now,
            UpdatedAt = now
        };

        _db.LicenseEntitlements.Add(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return MapEntitlementToModel(entity);
    }

    public async Task<bool> DeleteLicenseEntitlementAsync(
        string licenseId,
        string featureCode,
        CancellationToken ct = default)
    {
        var entity = await _db.LicenseEntitlements
            .FirstOrDefaultAsync(e => e.LicenseId == licenseId && e.FeatureCode == featureCode, ct)
            .ConfigureAwait(false);

        if (entity is null) return false;

        _db.LicenseEntitlements.Remove(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    // --- Active Feature Leases ---

    public async Task<IReadOnlyList<ActiveFeatureLeaseModel>> GetActiveFeaturesForLeaseAsync(
        string leaseId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseId);

        var entities = await _db.ActiveFeatureLeases.AsNoTracking()
            .Where(a => a.LeaseId == leaseId)
            .OrderBy(a => a.FeatureCode)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return entities.Select(MapActiveLeaseToModel).ToList();
    }

    public async Task<int> GetActiveFeatureCountAsync(
        string licenseId,
        string featureCode,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        return await _db.ActiveFeatureLeases.AsNoTracking()
            .CountAsync(a => a.LicenseId == licenseId && a.FeatureCode == featureCode && a.ExpiresAt > now, ct)
            .ConfigureAwait(false);
    }

    public async Task<ActiveFeatureLeaseModel?> TryAcquireFeatureLeaseAsync(
        FeatureAcquisitionRequest request,
        int? maxSeats,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        string featureCode = request.FeatureCode.Trim().ToUpperInvariant();

        // 1. Clean up expired feature leases for this license
        var expired = await _db.ActiveFeatureLeases
            .Where(a => a.LicenseId == request.LicenseId && a.ExpiresAt <= now)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (expired.Count > 0)
        {
            _db.ActiveFeatureLeases.RemoveRange(expired);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        // 2. Check if this lease already holds this feature
        var existing = await _db.ActiveFeatureLeases
            .FirstOrDefaultAsync(a => a.LeaseId == request.LeaseId && a.FeatureCode == featureCode, ct)
            .ConfigureAwait(false);

        if (existing is not null)
        {
            existing.ExpiresAt = now + request.Ttl;
            existing.AcquiredVersion = request.Version ?? existing.AcquiredVersion;
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return MapActiveLeaseToModel(existing);
        }

        // 3. Check seat limit if defined
        if (maxSeats.HasValue)
        {
            int currentActive = await _db.ActiveFeatureLeases
                .CountAsync(a => a.LicenseId == request.LicenseId && a.FeatureCode == featureCode && a.ExpiresAt > now, ct)
                .ConfigureAwait(false);

            if (currentActive >= maxSeats.Value)
            {
                return null; // capacity exceeded
            }
        }

        // 4. Create new active feature lease
        var newLease = new ActiveFeatureLeaseEntity
        {
            Id = Guid.NewGuid().ToString("N"),
            TenantId = request.TenantId,
            LicenseId = request.LicenseId,
            LeaseId = request.LeaseId,
            FeatureCode = featureCode,
            AcquiredVersion = request.Version,
            AcquiredAt = now,
            ExpiresAt = now + request.Ttl
        };

        _db.ActiveFeatureLeases.Add(newLease);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);

        return MapActiveLeaseToModel(newLease);
    }

    public async Task<bool> ReleaseFeatureLeaseAsync(
        string leaseId,
        string featureCode,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseId);
        ArgumentException.ThrowIfNullOrWhiteSpace(featureCode);

        string normalizedCode = featureCode.Trim().ToUpperInvariant();

        var existing = await _db.ActiveFeatureLeases
            .FirstOrDefaultAsync(a => a.LeaseId == leaseId && a.FeatureCode == normalizedCode, ct)
            .ConfigureAwait(false);

        if (existing is null) return false;

        _db.ActiveFeatureLeases.Remove(existing);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    public async Task<int> ReleaseAllFeaturesForLeaseAsync(
        string leaseId,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(leaseId);

        var existingList = await _db.ActiveFeatureLeases
            .Where(a => a.LeaseId == leaseId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (existingList.Count == 0) return 0;

        _db.ActiveFeatureLeases.RemoveRange(existingList);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return existingList.Count;
    }

    public async Task<IReadOnlyList<FeatureUsageMetric>> GetFeatureUsageMetricsAsync(
        string? tenantId,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var defs = await ListFeatureDefinitionsAsync(tenantId, null, ct).ConfigureAwait(false);

        var activeLeasesQuery = _db.ActiveFeatureLeases.AsNoTracking().Where(a => a.ExpiresAt > now);
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            activeLeasesQuery = activeLeasesQuery.Where(a => a.TenantId == tenantId);
        }

        var activeLeases = await activeLeasesQuery.ToListAsync(ct).ConfigureAwait(false);

        var result = new List<FeatureUsageMetric>();
        foreach (var def in defs)
        {
            var leasesForFeat = activeLeases.Where(a => string.Equals(a.FeatureCode, def.Code, StringComparison.OrdinalIgnoreCase)).ToList();
            result.Add(new FeatureUsageMetric(
                FeatureCode: def.Code,
                Name: def.Name,
                ProductCode: def.ProductId,
                MaxSeats: def.DefaultMaxSeats,
                InUseSeats: leasesForFeat.Count,
                DenialsCount: 0,
                ActiveLeaseIds: leasesForFeat.Select(l => l.LeaseId).ToList()));
        }

        return result;
    }

    // --- Helpers ---

    private static FeatureDefinitionModel MapFeatureToModel(FeatureDefinitionEntity e) =>
        new(e.Id, e.TenantId, e.ProductId, e.Code, e.Name, e.Description, e.MinVersion, e.MaxVersion, e.IsFloating, e.DefaultMaxSeats, e.CreatedAt, e.UpdatedAt);

    private static PackageSuiteModel MapSuiteToModel(PackageSuiteEntity s)
    {
        var list = new List<string>();
        if (!string.IsNullOrWhiteSpace(s.FeatureCodesJson))
        {
            try
            {
                list = JsonSerializer.Deserialize<List<string>>(s.FeatureCodesJson, JsonOptions) ?? new List<string>();
            }
            catch (JsonException)
            {
                list = new List<string>();
            }
        }

        return new PackageSuiteModel(s.Id, s.TenantId, s.ProductId, s.Code, s.Name, s.Description, list, s.CreatedAt, s.UpdatedAt);
    }

    private static LicenseEntitlementModel MapEntitlementToModel(LicenseEntitlementEntity e) =>
        new(e.Id, e.TenantId, e.LicenseId, e.FeatureCode, e.MaxSeats, e.AllowedVersionRange, e.IsEnabled, e.ParametersJson, e.CreatedAt, e.UpdatedAt);

    private static ActiveFeatureLeaseModel MapActiveLeaseToModel(ActiveFeatureLeaseEntity a) =>
        new(a.Id, a.TenantId, a.LicenseId, a.LeaseId, a.FeatureCode, a.AcquiredVersion, a.AcquiredAt, a.ExpiresAt);
}
