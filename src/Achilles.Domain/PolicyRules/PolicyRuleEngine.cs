namespace Achilles.Domain.PolicyRules;

public static class PolicyRuleEngine
{
    public static RuleEvaluationResult Evaluate(PolicyRuleSet ruleSet, RuleEvaluationContext context)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);
        ArgumentNullException.ThrowIfNull(context);

        // Build lookup for client groups
        var clientGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in ruleSet.Groups)
        {
            if (!string.IsNullOrWhiteSpace(context.UserId) &&
                g.Members.Any(m => WildcardMatcher.Matches(context.UserId, m)))
            {
                clientGroups.Add(g.Name);
            }

            if (!string.IsNullOrWhiteSpace(context.ClientIp) &&
                g.Members.Any(m => SubnetMatcher.MatchesSubnet(context.ClientIp, m)))
            {
                clientGroups.Add(g.Name);
            }

            if (g.Hosts is { Count: > 0 })
            {
                string? hostOrMachine = context.HostName ?? context.MachineId;
                if (hostOrMachine != null && g.Hosts.Any(h => WildcardMatcher.Matches(hostOrMachine, h)))
                {
                    clientGroups.Add(g.Name);
                }
            }
        }

        // ====================================================================
        // STEP 1 (FLT-24): DENY rules (First matching deny terminates immediately)
        // ====================================================================
        foreach (var rule in ruleSet.Rules.Where(r => r.Type == PolicyRuleType.Deny))
        {
            if (!FeatureMatches(rule.Feature, context.Features))
            {
                continue;
            }

            bool isMatch = false;

            // Check hosts
            if (rule.Hosts is { Count: > 0 })
            {
                string? hostOrMachine = context.HostName ?? context.MachineId;
                if (hostOrMachine != null && rule.Hosts.Any(h => WildcardMatcher.Matches(hostOrMachine, h)))
                {
                    isMatch = true;
                }
            }

            // Check users
            if (!isMatch && rule.Users is { Count: > 0 } && !string.IsNullOrWhiteSpace(context.UserId))
            {
                if (rule.Users.Any(u => WildcardMatcher.Matches(context.UserId, u)))
                {
                    isMatch = true;
                }
            }

            // Check groups
            if (!isMatch && rule.Groups is { Count: > 0 })
            {
                if (rule.Groups.Any(clientGroups.Contains))
                {
                    isMatch = true;
                }
            }

            // Check subnets
            if (!isMatch && rule.Subnets is { Count: > 0 } && !string.IsNullOrWhiteSpace(context.ClientIp))
            {
                if (rule.Subnets.Any(s => SubnetMatcher.MatchesSubnet(context.ClientIp, s)))
                {
                    isMatch = true;
                }
            }

            // Check Target object if present
            if (!isMatch && rule.Target is { } target)
            {
                if (!string.IsNullOrEmpty(target.User) && WildcardMatcher.Matches(context.UserId, target.User))
                {
                    isMatch = true;
                }
                else if (!string.IsNullOrEmpty(target.Group) && clientGroups.Contains(target.Group))
                {
                    isMatch = true;
                }
                else if (!string.IsNullOrEmpty(target.Host) && WildcardMatcher.Matches(context.HostName ?? context.MachineId, target.Host))
                {
                    isMatch = true;
                }
                else if (!string.IsNullOrEmpty(target.Subnet) && SubnetMatcher.MatchesSubnet(context.ClientIp, target.Subnet))
                {
                    isMatch = true;
                }
            }

            if (isMatch)
            {
                // FLT-24: First matching deny terminates evaluation immediately.
                return RuleEvaluationResult.Deny(rule.Reason ?? "Access denied by policy rule.");
            }
        }

        // ====================================================================
        // STEP 2 (FLT-24): MAX limit rules
        // ====================================================================
        foreach (var rule in ruleSet.Rules.Where(r => r.Type == PolicyRuleType.Max && r.Value.HasValue))
        {
            if (!FeatureMatches(rule.Feature, context.Features))
            {
                continue;
            }

            string? matchedTarget = null;
            if (rule.Target is { } target)
            {
                if (!string.IsNullOrEmpty(target.Group) && clientGroups.Contains(target.Group))
                    matchedTarget = $"group:{target.Group}";
                else if (!string.IsNullOrEmpty(target.User) && WildcardMatcher.Matches(context.UserId, target.User))
                    matchedTarget = $"user:{target.User}";
                else if (!string.IsNullOrEmpty(target.Subnet) && SubnetMatcher.MatchesSubnet(context.ClientIp, target.Subnet))
                    matchedTarget = $"subnet:{target.Subnet}";
                else if (!string.IsNullOrEmpty(target.Host) && WildcardMatcher.Matches(context.HostName ?? context.MachineId, target.Host))
                    matchedTarget = $"host:{target.Host}";
            }
            else if (rule.Groups is { Count: > 0 })
            {
                var g = rule.Groups.FirstOrDefault(clientGroups.Contains);
                if (g != null) matchedTarget = $"group:{g}";
            }
            else if (rule.Users is { Count: > 0 } && !string.IsNullOrWhiteSpace(context.UserId))
            {
                var u = rule.Users.FirstOrDefault(x => WildcardMatcher.Matches(context.UserId, x));
                if (u != null) matchedTarget = $"user:{u}";
            }

            if (matchedTarget != null && context.GetActiveCountForTarget != null)
            {
                int activeCount = context.GetActiveCountForTarget(matchedTarget);
                int maxVal = rule.Value.GetValueOrDefault();
                if (activeCount + context.CurrentlyHeldByClient >= maxVal)
                {
                    return RuleEvaluationResult.MaxExceeded(matchedTarget, maxVal);
                }
            }
        }

        // ====================================================================
        // STEP 3 (FLT-24): RESERVE rules
        // ====================================================================
        string? matchedReservationTarget = null;
        foreach (var rule in ruleSet.Rules.Where(r => r.Type == PolicyRuleType.Reserve && r.Value.HasValue))
        {
            if (!FeatureMatches(rule.Feature, context.Features))
            {
                continue;
            }

            if (rule.Target is { } target)
            {
                if (!string.IsNullOrEmpty(target.Group) && clientGroups.Contains(target.Group))
                {
                    matchedReservationTarget = target.Group;
                    break;
                }
                if (!string.IsNullOrEmpty(target.User) && WildcardMatcher.Matches(context.UserId, target.User))
                {
                    matchedReservationTarget = target.User;
                    break;
                }
            }
            else if (rule.Groups is { Count: > 0 })
            {
                var g = rule.Groups.FirstOrDefault(clientGroups.Contains);
                if (g != null)
                {
                    matchedReservationTarget = g;
                    break;
                }
            }
            else if (rule.Users is { Count: > 0 } && !string.IsNullOrWhiteSpace(context.UserId))
            {
                var u = rule.Users.FirstOrDefault(x => WildcardMatcher.Matches(context.UserId, x));
                if (u != null)
                {
                    matchedReservationTarget = u;
                    break;
                }
            }
        }

        // ====================================================================
        // STEP 4 (FLT-24): PRIORITY rules
        // ====================================================================
        int? resolvedPriority = null;
        foreach (var rule in ruleSet.Rules.Where(r => r.Type == PolicyRuleType.Priority && r.Value.HasValue))
        {
            bool applies = false;
            if (rule.Target is { } target)
            {
                if (!string.IsNullOrEmpty(target.Group) && clientGroups.Contains(target.Group))
                    applies = true;
                else if (!string.IsNullOrEmpty(target.User) && WildcardMatcher.Matches(context.UserId, target.User))
                    applies = true;
            }
            else if (rule.Groups is { Count: > 0 } && rule.Groups.Any(clientGroups.Contains))
            {
                applies = true;
            }
            else if (rule.Users is { Count: > 0 } && !string.IsNullOrWhiteSpace(context.UserId) &&
                     rule.Users.Any(u => WildcardMatcher.Matches(context.UserId, u)))
            {
                applies = true;
            }

            if (applies)
            {
                resolvedPriority = Math.Max(resolvedPriority ?? 0, rule.Value.GetValueOrDefault());
            }
        }

        return RuleEvaluationResult.Allow(resolvedPriority, matchedReservationTarget);
    }

    private static bool FeatureMatches(string? ruleFeature, IReadOnlyList<string>? requestedFeatures)
    {
        if (string.IsNullOrEmpty(ruleFeature) || ruleFeature == "*")
        {
            return true;
        }

        if (requestedFeatures == null || requestedFeatures.Count == 0)
        {
            return false;
        }

        return requestedFeatures.Any(f => string.Equals(f, ruleFeature, StringComparison.OrdinalIgnoreCase));
    }
}
