namespace Achilles.Domain.Experiments;

public interface IExperimentStore
{
    Task<IReadOnlyList<Experiment>> GetAllAsync(string? tenantId = null, CancellationToken ct = default);
    Task<IReadOnlyList<Experiment>> GetActiveAsync(string? tenantId = null, CancellationToken ct = default);
    Task<Experiment?> GetByIdAsync(string id, CancellationToken ct = default);
    Task SaveAsync(Experiment experiment, string? tenantId = null, CancellationToken ct = default);
    Task<bool> DeleteAsync(string id, CancellationToken ct = default);
    Task RecordMetricAsync(
        string experimentId,
        string variantId,
        bool isSuccess,
        bool isRenewal,
        bool isDenial,
        bool isError,
        double latencyMs,
        CancellationToken ct = default);
    Task<IReadOnlyList<VariantMetrics>> GetMetricsAsync(string experimentId, CancellationToken ct = default);
    Task ResetMetricsAsync(string experimentId, CancellationToken ct = default);
}
