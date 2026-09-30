using System.Text.Json.Serialization;

namespace Symbolon.Domain.Experiments;

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ExperimentStatus
{
    Draft,
    Active,
    Paused,
    Completed,
    RolledBack
}

public sealed record ExperimentOverrides
{
    public int? LeaseTtlSeconds { get; init; }
    public int? HeartbeatIntervalSeconds { get; init; }
    public int? GraceTtlSeconds { get; init; }
    public string? PolicyRulesYaml { get; init; }
    public string? FeatureTier { get; init; }
    public IReadOnlyDictionary<string, string> Metadata { get; init; } = new Dictionary<string, string>();
}

public sealed record ExperimentVariant
{
    public required string VariantId { get; init; }
    public required string Name { get; init; }
    public int Weight { get; init; } = 50;
    public bool IsControl { get; init; }
    public ExperimentOverrides Overrides { get; init; } = new();
}

public sealed record ExperimentTargeting
{
    public IReadOnlyList<string> TenantIds { get; init; } = [];
    public IReadOnlyList<string> LicensePrefixes { get; init; } = [];
    public IReadOnlyList<string> SdkLanguages { get; init; } = [];
    public string? MinSdkVersion { get; init; }
    public IReadOnlyList<string> OsPlatforms { get; init; } = [];

    public bool Matches(string? tenantId, string licenseKey, ExperimentClientContext? context)
    {
        if (TenantIds.Count > 0 && (string.IsNullOrWhiteSpace(tenantId) || !TenantIds.Contains(tenantId, StringComparer.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (LicensePrefixes.Count > 0 && !LicensePrefixes.Any(p => licenseKey.StartsWith(p, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        if (context is not null)
        {
            if (SdkLanguages.Count > 0 && !string.IsNullOrWhiteSpace(context.SdkLanguage) &&
                !SdkLanguages.Contains(context.SdkLanguage, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }

            if (OsPlatforms.Count > 0 && !string.IsNullOrWhiteSpace(context.OsPlatform) &&
                !OsPlatforms.Contains(context.OsPlatform, StringComparer.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }
}

public sealed record ExperimentCircuitBreaker
{
    public double MaxErrorRate { get; init; } = 0.05; // 5% error rate trips circuit breaker
    public int MinSamplesThreshold { get; init; } = 50; // Minimum samples before tripping
    public bool AutoRollback { get; init; } = true;
}

public sealed record ExperimentClientContext
{
    public string? SdkLanguage { get; init; }
    public string? SdkVersion { get; init; }
    public string? OsPlatform { get; init; }
    public string? ClientIp { get; init; }
}

public sealed class Experiment
{
    public required string Id { get; init; }
    public string? TenantId { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }
    public ExperimentStatus Status { get; set; } = ExperimentStatus.Draft;
    public string Salt { get; init; } = Guid.NewGuid().ToString("N");
    public int TrafficAllocation { get; set; } = 100; // 0..100 %
    public ExperimentTargeting Targeting { get; set; } = new();
    public IReadOnlyList<ExperimentVariant> Variants { get; init; } = [];
    public ExperimentCircuitBreaker CircuitBreaker { get; set; } = new();
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public string? PromotedVariantId { get; set; }
}

public sealed record ExperimentEvaluationResult
{
    public required string ExperimentId { get; init; }
    public required string VariantId { get; init; }
    public required bool IsInExperiment { get; init; }
    public required bool IsControl { get; init; }
    public ExperimentOverrides Overrides { get; init; } = new();

    public static ExperimentEvaluationResult NotInExperiment(string experimentId) => new()
    {
        ExperimentId = experimentId,
        VariantId = "control_baseline",
        IsInExperiment = false,
        IsControl = true,
        Overrides = new ExperimentOverrides()
    };
}

public sealed record VariantMetrics
{
    public required string VariantId { get; init; }
    public long TotalRequests { get; init; }
    public long SuccessfulCheckouts { get; init; }
    public long Renewals { get; init; }
    public long Denials { get; init; }
    public long Errors { get; init; }
    public double LatencyMsSum { get; init; }
    public double LatencyMsSquareSum { get; init; }

    public double ErrorRate => TotalRequests > 0 ? (double)Errors / TotalRequests : 0.0;
    public double SuccessRate => TotalRequests > 0 ? (double)SuccessfulCheckouts / TotalRequests : 0.0;
    public double AverageLatencyMs => TotalRequests > 0 ? LatencyMsSum / TotalRequests : 0.0;

    public double LatencyStdDev
    {
        get
        {
            if (TotalRequests <= 1) return 0.0;
            double variance = (LatencyMsSquareSum - ((LatencyMsSum * LatencyMsSum) / TotalRequests)) / (TotalRequests - 1);
            return variance > 0 ? Math.Sqrt(variance) : 0.0;
        }
    }
}

public sealed record ExperimentStatisticalReport
{
    public required string ExperimentId { get; init; }
    public required ExperimentStatus Status { get; init; }
    public required string ControlVariantId { get; init; }
    public required string TreatmentVariantId { get; init; }
    public required VariantMetrics ControlMetrics { get; init; }
    public required VariantMetrics TreatmentMetrics { get; init; }

    // Conversion / Success rate comparison
    public double ZScore { get; init; }
    public double PValue { get; init; }
    public double DifferenceRate { get; init; }
    public double ConfidenceIntervalLower { get; init; }
    public double ConfidenceIntervalUpper { get; init; }
    public bool IsStatisticallySignificant { get; init; }

    // Latency comparison
    public double LatencyTScore { get; init; }
    public double LatencyPValue { get; init; }
    public double LatencyDifferenceMs { get; init; }

    public string Recommendation { get; init; } = "Insufficient data";
}
