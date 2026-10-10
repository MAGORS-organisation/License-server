using Achilles.Protocol;

namespace Achilles.Domain.Borrow;

public sealed record BorrowEvaluationResult(
    bool IsAllowed,
    string? FailureReason,
    string? ProblemType)
{
    public static BorrowEvaluationResult Allowed() => new(true, null, null);
    public static BorrowEvaluationResult Denied(string reason, string problemType) => new(false, reason, problemType);
}

/// <summary>
/// Evaluates policy constraints for offline seat borrowing according to FLT-18.
/// </summary>
public static class BorrowPolicyEvaluator
{
    public static BorrowEvaluationResult Evaluate(
        bool borrowEnabled,
        int maxDurationDays,
        int maxConcurrent,
        int requestedDays,
        int currentActiveBorrowsCount)
    {
        if (!borrowEnabled)
        {
            return BorrowEvaluationResult.Denied(
                "Offline seat borrowing is disabled by the license policy.",
                ProblemTypes.BorrowDisabled);
        }

        int effectiveMaxDuration = Math.Clamp(maxDurationDays, 1, 30);
        if (requestedDays < 1 || requestedDays > effectiveMaxDuration)
        {
            return BorrowEvaluationResult.Denied(
                $"Requested borrow duration of {requestedDays} days exceeds the policy limit of {effectiveMaxDuration} days (maximum 30 days per FLT-2/FLT-18).",
                ProblemTypes.BorrowDurationExceeded);
        }

        int effectiveMaxConcurrent = Math.Max(1, maxConcurrent);
        if (currentActiveBorrowsCount >= effectiveMaxConcurrent)
        {
            return BorrowEvaluationResult.Denied(
                $"Maximum concurrent offline borrowed seats limit ({effectiveMaxConcurrent}) has been reached.",
                ProblemTypes.BorrowLimitExceeded);
        }

        return BorrowEvaluationResult.Allowed();
    }
}
