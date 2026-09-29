namespace Symbolon.Domain.PolicyRules;

public enum PolicyRuleType
{
    Deny,
    Max,
    Reserve,
    Priority
}

public sealed record RuleTarget(
    string? Group = null,
    string? User = null,
    string? Subnet = null,
    string? Host = null);

public sealed record PolicyRule(
    PolicyRuleType Type,
    int? Value,
    RuleTarget? Target,
    IReadOnlyList<string>? Hosts = null,
    IReadOnlyList<string>? Users = null,
    IReadOnlyList<string>? Groups = null,
    IReadOnlyList<string>? Subnets = null,
    string? Feature = null,
    string? Reason = null);

public sealed record PolicyRuleGroup(
    string Name,
    IReadOnlyList<string> Members,
    IReadOnlyList<string>? Hosts = null);

public sealed record PolicyRuleSet(
    int Version,
    string? LicenseId,
    IReadOnlyList<PolicyRuleGroup> Groups,
    IReadOnlyList<PolicyRule> Rules);

public sealed record RuleEvaluationContext(
    string LicenseId,
    string? UserId = null,
    string? MachineId = null,
    string? HostName = null,
    string? ClientIp = null,
    IReadOnlyList<string>? Features = null,
    int CurrentlyHeldByClient = 0,
    Func<string, int>? GetActiveCountForTarget = null);

public sealed record RuleEvaluationResult(
    bool Allowed,
    string? DenyReason,
    string? DenyType,
    int? ResolvedPriority,
    string? MatchedReservationTarget,
    int? MaxLimit)
{
    public static RuleEvaluationResult Allow(int? priority = null, string? reservationTarget = null) =>
        new(true, null, null, priority, reservationTarget, null);

    public static RuleEvaluationResult Deny(string reason, string denyType = "rule-denied") =>
        new(false, reason, denyType, null, null, null);

    public static RuleEvaluationResult MaxExceeded(string targetName, int maxLimit) =>
        new(false, $"Maximum limit of {maxLimit} seats reached for '{targetName}'.", "group-quota-exceeded", null, null, maxLimit);
}
