using System.Globalization;

namespace Achilles.Protocol;

/// <summary>
/// Standard machine matching strategies according to spec/08-fingerprint.md (FPR-5 to FPR-9).
/// </summary>
public static class MatchingStrategies
{
    public const string MatchAny = "match-any";
    public const string MatchTwo = "match-two";
    public const string MatchMost = "match-most";
    public const string MatchAll = "match-all";

    public static readonly IReadOnlyList<string> All =
    [
        MatchAny,
        MatchTwo,
        MatchMost,
        MatchAll
    ];

    public static bool IsValid(string? strategy) =>
        !string.IsNullOrWhiteSpace(strategy) &&
        All.Contains(strategy.Trim(), StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Evaluation result of a machine fingerprint matching comparison.
/// </summary>
public sealed record FingerprintMatchResult
{
    public required bool IsMatch { get; init; }
    public required string StrategyUsed { get; init; }
    public required int CommonComponentsCount { get; init; }
    public required int MatchedComponentsCount { get; init; }
    public required IReadOnlyList<string> MatchedKeys { get; init; }
    public required IReadOnlyList<string> MismatchedKeys { get; init; }
    public string? FailureReason { get; init; }

    public double MatchRatio => CommonComponentsCount == 0 ? 0.0 : (double)MatchedComponentsCount / CommonComponentsCount;
}

/// <summary>
/// Normative machine fingerprint matching engine implementing FPR-5, FPR-6, FPR-7, FPR-8, and FPR-9.
/// </summary>
public static class FingerprintMatchingEngine
{
    /// <summary>
    /// Evaluates whether the incoming fingerprint matches the stored machine fingerprint
    /// according to the specified matching strategy and FPR-5 through FPR-8 rules.
    /// </summary>
    public static FingerprintMatchResult EvaluateMatch(
        IReadOnlyDictionary<string, string>? storedComponents,
        IReadOnlyDictionary<string, string>? incomingComponents,
        string? strategy = null)
    {
        // FPR-6: Default strategy MUST be match-most.
        string effectiveStrategy = string.IsNullOrWhiteSpace(strategy)
            ? MatchingStrategies.MatchMost
            : strategy.Trim();

        var matchedCanonicalStrategy = MatchingStrategies.All
            .FirstOrDefault(s => string.Equals(s, effectiveStrategy, StringComparison.OrdinalIgnoreCase))
            ?? MatchingStrategies.MatchMost;
        effectiveStrategy = matchedCanonicalStrategy;

        var cleanStored = FingerprintHelper.FilterValidComponents(storedComponents);
        var cleanIncoming = FingerprintHelper.FilterValidComponents(incomingComponents);

        // FPR-7: Only components present in BOTH compared fingerprints are counted.
        var commonKeys = cleanStored.Keys
            .Intersect(cleanIncoming.Keys, StringComparer.OrdinalIgnoreCase)
            .OrderBy(k => k, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (commonKeys.Count == 0)
        {
            return new FingerprintMatchResult
            {
                IsMatch = false,
                StrategyUsed = effectiveStrategy,
                CommonComponentsCount = 0,
                MatchedComponentsCount = 0,
                MatchedKeys = Array.Empty<string>(),
                MismatchedKeys = Array.Empty<string>(),
                FailureReason = "No common components found between fingerprints to compare (FPR-7)."
            };
        }

        var matchedKeys = new List<string>();
        var mismatchedKeys = new List<string>();

        foreach (var key in commonKeys)
        {
            string valStored = cleanStored[key].Trim();
            string valIncoming = cleanIncoming[key].Trim();

            if (string.Equals(valStored, valIncoming, StringComparison.OrdinalIgnoreCase))
            {
                matchedKeys.Add(key);
            }
            else
            {
                mismatchedKeys.Add(key);
            }
        }

        int commonCount = commonKeys.Count;
        int matchedCount = matchedKeys.Count;
        bool isMatch;
        string? failureReason = null;

        switch (effectiveStrategy)
        {
            case MatchingStrategies.MatchAny:
                // FPR-5: At least ONE component matches
                isMatch = matchedCount >= 1;
                if (!isMatch)
                {
                    failureReason = $"Expected at least 1 matching component for '{MatchingStrategies.MatchAny}', but matched 0 out of {commonCount} common components.";
                }
                break;

            case MatchingStrategies.MatchTwo:
                // FPR-5: At least TWO components match
                isMatch = matchedCount >= 2;
                if (!isMatch)
                {
                    failureReason = $"Expected at least 2 matching components for '{MatchingStrategies.MatchTwo}', but matched {matchedCount} out of {commonCount} common components.";
                }
                break;

            case MatchingStrategies.MatchMost:
                // FPR-8: If the number of common components is less than 2, match-most MUST behave as match-all!
                if (commonCount < 2)
                {
                    isMatch = matchedCount == commonCount;
                    if (!isMatch)
                    {
                        failureReason = $"Common components count ({commonCount}) is less than 2; under FPR-8 'match-most' degraded to 'match-all' and matched {matchedCount}/{commonCount}.";
                    }
                }
                else
                {
                    // Majority of present components in common: strictly greater than half
                    isMatch = matchedCount > (commonCount / 2.0);
                    if (!isMatch)
                    {
                        failureReason = $"Expected majority of matching components for '{MatchingStrategies.MatchMost}' (> {commonCount / 2.0:F1}), but matched {matchedCount} out of {commonCount} common components.";
                    }
                }
                break;

            case MatchingStrategies.MatchAll:
                // FPR-5: ALL present common components match
                isMatch = matchedCount == commonCount;
                if (!isMatch)
                {
                    failureReason = $"Expected all {commonCount} components to match for '{MatchingStrategies.MatchAll}', but only {matchedCount} matched ({string.Join(", ", mismatchedKeys)} mismatched).";
                }
                break;

            default:
                isMatch = matchedCount > (commonCount / 2.0);
                break;
        }

        return new FingerprintMatchResult
        {
            IsMatch = isMatch,
            StrategyUsed = effectiveStrategy,
            CommonComponentsCount = commonCount,
            MatchedComponentsCount = matchedCount,
            MatchedKeys = matchedKeys,
            MismatchedKeys = mismatchedKeys,
            FailureReason = failureReason
        };
    }
}
