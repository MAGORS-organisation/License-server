using System.IO.Enumeration;
using System.Net;

namespace Achilles.Domain.PolicyRules;

public static class WildcardMatcher
{
    public static bool Matches(string? value, string? pattern)
    {
        if (value is null || pattern is null)
        {
            return false;
        }

        if (pattern == "*" || string.Equals(pattern, value, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return FileSystemName.MatchesSimpleExpression(pattern, value, ignoreCase: true);
    }
}

public static class SubnetMatcher
{
    public static bool MatchesSubnet(string? ipString, string? cidrOrWildcard)
    {
        if (string.IsNullOrWhiteSpace(ipString) || string.IsNullOrWhiteSpace(cidrOrWildcard))
        {
            return false;
        }

        string trimmedIp = ipString.Trim();
        string pattern = cidrOrWildcard.Trim();

        // 1. CIDR notation (IPv4 or IPv6) e.g., "192.168.100.0/24"
        if (pattern.Contains('/', StringComparison.Ordinal))
        {
            if (IPAddress.TryParse(trimmedIp, out var ip) && IPNetwork.TryParse(pattern, out var network))
            {
                return network.Contains(ip);
            }
            return false;
        }

        // 2. Wildcard notation e.g., "192.168.100.*"
        if (pattern.Contains('*', StringComparison.Ordinal) || pattern.Contains('?', StringComparison.Ordinal))
        {
            return WildcardMatcher.Matches(trimmedIp, pattern);
        }

        // 3. Exact IP match
        if (IPAddress.TryParse(trimmedIp, out var clientIp) && IPAddress.TryParse(pattern, out var targetIp))
        {
            return clientIp.Equals(targetIp);
        }

        return string.Equals(trimmedIp, pattern, StringComparison.OrdinalIgnoreCase);
    }
}
