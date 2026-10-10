using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Achilles.Domain.Experiments;

public static class DeterministicBucketRouter
{
    /// <summary>
    /// Calculates a deterministic, stateless bucket [0..99] for a given license key, machine ID, and experiment salt.
    /// Uses cryptographically uniform SHA-256 hashing.
    /// </summary>
    public static int CalculateBucket(string licenseKey, string machineId, string salt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(licenseKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(machineId);

        string input = $"{licenseKey.Trim().ToUpperInvariant()}:{machineId.Trim()}:{salt}";
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(hash.AsSpan(0, 4));
        return (int)(value % 100);
    }

    /// <summary>
    /// Evaluates an experiment and assigns the client to an experiment variant or baseline.
    /// Adheres strictly to the Sticky Session Invariant: zero variant drift across repeat evaluations.
    /// </summary>
    public static ExperimentEvaluationResult Route(
        Experiment experiment,
        string? tenantId,
        string licenseKey,
        string machineId,
        ExperimentClientContext? context = null)
    {
        ArgumentNullException.ThrowIfNull(experiment);
        ArgumentException.ThrowIfNullOrWhiteSpace(licenseKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(machineId);

        // If experiment was completed and a variant was promoted to 100%, always use it
        if (experiment.Status == ExperimentStatus.Completed && !string.IsNullOrWhiteSpace(experiment.PromotedVariantId))
        {
            var promoted = experiment.Variants.FirstOrDefault(v => v.VariantId == experiment.PromotedVariantId);
            if (promoted is not null)
            {
                return new ExperimentEvaluationResult
                {
                    ExperimentId = experiment.Id,
                    VariantId = promoted.VariantId,
                    IsInExperiment = true,
                    IsControl = promoted.IsControl,
                    Overrides = promoted.Overrides
                };
            }
        }

        // If experiment is not currently Active, return baseline
        if (experiment.Status != ExperimentStatus.Active)
        {
            return ExperimentEvaluationResult.NotInExperiment(experiment.Id);
        }

        // Check targeting rules (tenants, prefixes, SDKs, OS)
        if (!experiment.Targeting.Matches(tenantId, licenseKey, context))
        {
            return ExperimentEvaluationResult.NotInExperiment(experiment.Id);
        }

        // Check traffic allocation
        int bucket = CalculateBucket(licenseKey, machineId, experiment.Salt);
        if (bucket >= experiment.TrafficAllocation || experiment.TrafficAllocation <= 0)
        {
            return ExperimentEvaluationResult.NotInExperiment(experiment.Id);
        }

        if (experiment.Variants.Count == 0)
        {
            return ExperimentEvaluationResult.NotInExperiment(experiment.Id);
        }

        int totalWeight = experiment.Variants.Sum(v => Math.Max(0, v.Weight));
        if (totalWeight <= 0)
        {
            return ExperimentEvaluationResult.NotInExperiment(experiment.Id);
        }

        // Scale bucket [0..TrafficAllocation - 1] to target point [0..totalWeight - 1]
        long scaledPoint = ((long)bucket * totalWeight) / experiment.TrafficAllocation;

        int accumulatedWeight = 0;
        foreach (var variant in experiment.Variants)
        {
            accumulatedWeight += Math.Max(0, variant.Weight);
            if (scaledPoint < accumulatedWeight)
            {
                return new ExperimentEvaluationResult
                {
                    ExperimentId = experiment.Id,
                    VariantId = variant.VariantId,
                    IsInExperiment = true,
                    IsControl = variant.IsControl,
                    Overrides = variant.Overrides
                };
            }
        }

        var fallbackVariant = experiment.Variants[^1];
        return new ExperimentEvaluationResult
        {
            ExperimentId = experiment.Id,
            VariantId = fallbackVariant.VariantId,
            IsInExperiment = true,
            IsControl = fallbackVariant.IsControl,
            Overrides = fallbackVariant.Overrides
        };
    }
}
