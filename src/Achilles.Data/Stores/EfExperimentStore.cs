using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Achilles.Data.Entities;
using Achilles.Domain.Experiments;

namespace Achilles.Data.Stores;

public sealed class EfExperimentStore : IExperimentStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    private readonly AchillesDbContext _db;

    public EfExperimentStore(AchillesDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<IReadOnlyList<Experiment>> GetAllAsync(string? tenantId = null, CancellationToken ct = default)
    {
        var query = _db.Experiments.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            query = query.Where(e => e.TenantId == tenantId || e.TenantId == null);
        }

        var entities = await query.OrderByDescending(e => e.CreatedAt).ToListAsync(ct).ConfigureAwait(false);
        return entities.Select(ToDomain).ToList();
    }

    public async Task<IReadOnlyList<Experiment>> GetActiveAsync(string? tenantId = null, CancellationToken ct = default)
    {
        var activeStatus = ExperimentStatus.Active.ToString();
        var query = _db.Experiments.AsNoTracking().Where(e => e.Status == activeStatus);
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            query = query.Where(e => e.TenantId == tenantId || e.TenantId == null);
        }

        var entities = await query.ToListAsync(ct).ConfigureAwait(false);
        return entities.Select(ToDomain).ToList();
    }

    public async Task<Experiment?> GetByIdAsync(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var entity = await _db.Experiments.AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == id, ct)
            .ConfigureAwait(false);

        return entity is not null ? ToDomain(entity) : null;
    }

    public async Task SaveAsync(Experiment experiment, string? tenantId = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(experiment);

        var entity = await _db.Experiments
            .FirstOrDefaultAsync(e => e.Id == experiment.Id, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            entity = new ExperimentEntity
            {
                Id = experiment.Id,
                TenantId = tenantId ?? experiment.TenantId,
                CreatedAt = experiment.CreatedAt
            };
            _db.Experiments.Add(entity);
        }

        entity.Name = experiment.Name;
        entity.Description = experiment.Description;
        entity.Status = experiment.Status.ToString();
        entity.Salt = experiment.Salt;
        entity.TrafficAllocation = experiment.TrafficAllocation;
        entity.PromotedVariantId = experiment.PromotedVariantId;
        entity.StartedAt = experiment.StartedAt;
        entity.EndedAt = experiment.EndedAt;
        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            entity.TenantId = tenantId;
        }

        entity.TargetingJson = JsonSerializer.Serialize(experiment.Targeting, JsonOptions);
        entity.VariantsJson = JsonSerializer.Serialize(experiment.Variants, JsonOptions);
        entity.CircuitBreakerJson = JsonSerializer.Serialize(experiment.CircuitBreaker, JsonOptions);

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<bool> DeleteAsync(string id, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);

        var entity = await _db.Experiments
            .FirstOrDefaultAsync(e => e.Id == id, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            return false;
        }

        _db.Experiments.Remove(entity);
        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    public async Task RecordMetricAsync(
        string experimentId,
        string variantId,
        bool isSuccess,
        bool isRenewal,
        bool isDenial,
        bool isError,
        double latencyMs,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(experimentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(variantId);

        var metric = await _db.ExperimentMetrics
            .FirstOrDefaultAsync(m => m.ExperimentId == experimentId && m.VariantId == variantId, ct)
            .ConfigureAwait(false);

        if (metric is null)
        {
            metric = new ExperimentMetricEntity
            {
                Id = Guid.NewGuid().ToString("N"),
                ExperimentId = experimentId,
                VariantId = variantId,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            _db.ExperimentMetrics.Add(metric);
        }

        metric.TotalRequests++;
        if (isSuccess) metric.SuccessfulCheckouts++;
        if (isRenewal) metric.Renewals++;
        if (isDenial) metric.Denials++;
        if (isError) metric.Errors++;
        metric.LatencyMsSum += latencyMs;
        metric.LatencyMsSquareSum += (latencyMs * latencyMs);
        metric.UpdatedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<VariantMetrics>> GetMetricsAsync(string experimentId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(experimentId);

        var entities = await _db.ExperimentMetrics.AsNoTracking()
            .Where(m => m.ExperimentId == experimentId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return entities.Select(m => new VariantMetrics
        {
            VariantId = m.VariantId,
            TotalRequests = m.TotalRequests,
            SuccessfulCheckouts = m.SuccessfulCheckouts,
            Renewals = m.Renewals,
            Denials = m.Denials,
            Errors = m.Errors,
            LatencyMsSum = m.LatencyMsSum,
            LatencyMsSquareSum = m.LatencyMsSquareSum
        }).ToList();
    }

    public async Task ResetMetricsAsync(string experimentId, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(experimentId);

        var entities = await _db.ExperimentMetrics
            .Where(m => m.ExperimentId == experimentId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (entities.Count > 0)
        {
            _db.ExperimentMetrics.RemoveRange(entities);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
        }
    }

    private static Experiment ToDomain(ExperimentEntity entity)
    {
        Enum.TryParse<ExperimentStatus>(entity.Status, true, out var status);

        var targeting = !string.IsNullOrWhiteSpace(entity.TargetingJson)
            ? JsonSerializer.Deserialize<ExperimentTargeting>(entity.TargetingJson, JsonOptions) ?? new ExperimentTargeting()
            : new ExperimentTargeting();

        var variants = !string.IsNullOrWhiteSpace(entity.VariantsJson)
            ? JsonSerializer.Deserialize<List<ExperimentVariant>>(entity.VariantsJson, JsonOptions) ?? []
            : [];

        var circuitBreaker = !string.IsNullOrWhiteSpace(entity.CircuitBreakerJson)
            ? JsonSerializer.Deserialize<ExperimentCircuitBreaker>(entity.CircuitBreakerJson, JsonOptions) ?? new ExperimentCircuitBreaker()
            : new ExperimentCircuitBreaker();

        return new Experiment
        {
            Id = entity.Id,
            TenantId = entity.TenantId,
            Name = entity.Name,
            Description = entity.Description,
            Status = status,
            Salt = entity.Salt,
            TrafficAllocation = entity.TrafficAllocation,
            Targeting = targeting,
            Variants = variants,
            CircuitBreaker = circuitBreaker,
            CreatedAt = entity.CreatedAt,
            StartedAt = entity.StartedAt,
            EndedAt = entity.EndedAt,
            PromotedVariantId = entity.PromotedVariantId
        };
    }
}
