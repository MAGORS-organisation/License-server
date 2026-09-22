using System.Globalization;

namespace Symbolon.Domain.Migration;

public sealed record TranspiledGroup(string Name, IReadOnlyList<string> Members);

public sealed record TranspiledSeatAllocation(
    string FeatureCode,
    string TargetType, // "Group", "User", "Host"
    string TargetName,
    string AllocationType, // "Reservation" (guaranteed), "Limit" (max cap)
    int Seats);

public sealed record TranspiledEntitlement(
    string FeatureCode,
    string TargetType, // "Group", "User", "Host"
    string TargetName,
    string Effect); // "Include", "Exclude"

public sealed record TranspiledBorrowingPolicy(
    bool Enabled,
    int MaxBorrowDurationDays,
    int MinAvailableSeatsForBorrow);

public sealed record TranspiledPolicyModel(
    IReadOnlyList<TranspiledGroup> UserGroups,
    IReadOnlyList<TranspiledGroup> HostGroups,
    IReadOnlyList<TranspiledSeatAllocation> SeatAllocations,
    IReadOnlyList<TranspiledEntitlement> Entitlements,
    TranspiledBorrowingPolicy Borrowing,
    int? IdleTimeoutSeconds,
    int? LingerSeconds,
    IReadOnlyDictionary<string, int> FeatureTimeouts);

public sealed record OptionsTranspilationReport(
    TranspiledPolicyModel Policy,
    int TotalRulesParsed,
    IReadOnlyList<string> RecognizedDirectives,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Recommendations);

public static class FlexNetOptionsTranspiler
{
    public static OptionsTranspilationReport Transpile(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var userGroups = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var hostGroups = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var allocations = new List<TranspiledSeatAllocation>();
        var entitlements = new List<TranspiledEntitlement>();
        var featureTimeouts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        int? globalTimeout = null;
        int? linger = null;
        int borrowMinSeats = 0;
        int maxBorrowDays = 14;
        bool borrowingEnabled = false;

        int parsedRulesCount = 0;
        var recognized = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var warnings = new List<string>();
        var recommendations = new List<string>();

        using var reader = new StringReader(content);
        string? rawLine;

        while ((rawLine = reader.ReadLine()) is not null)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#'))
            {
                continue;
            }

            var parts = line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;

            string directive = parts[0].ToUpperInvariant();

            switch (directive)
            {
                case "GROUP":
                    // GROUP <name> <user1> <user2> ...
                    if (parts.Length >= 3)
                    {
                        string groupName = parts[1];
                        if (!userGroups.TryGetValue(groupName, out var members))
                        {
                            members = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            userGroups[groupName] = members;
                        }

                        for (int i = 2; i < parts.Length; i++)
                        {
                            members.Add(parts[i]);
                        }
                        recognized.Add("GROUP");
                        parsedRulesCount++;
                    }
                    break;

                case "HOST_GROUP":
                    // HOST_GROUP <name> <host1> <host2> ...
                    if (parts.Length >= 3)
                    {
                        string groupName = parts[1];
                        if (!hostGroups.TryGetValue(groupName, out var hosts))
                        {
                            hosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                            hostGroups[groupName] = hosts;
                        }

                        for (int i = 2; i < parts.Length; i++)
                        {
                            hosts.Add(parts[i]);
                        }
                        recognized.Add("HOST_GROUP");
                        parsedRulesCount++;
                    }
                    break;

                case "RESERVE":
                    // RESERVE <count> <feature> [GROUP|USER|HOST] <target>
                    if (parts.Length >= 5 && int.TryParse(parts[1], CultureInfo.InvariantCulture, out int resSeats))
                    {
                        string feat = parts[2].ToUpperInvariant();
                        string targetType = parts[3].ToUpperInvariant();
                        string targetName = parts[4];

                        allocations.Add(new TranspiledSeatAllocation(
                            FeatureCode: feat,
                            TargetType: NormalizeTargetType(targetType),
                            TargetName: targetName,
                            AllocationType: "Reservation",
                            Seats: resSeats));

                        recognized.Add("RESERVE");
                        parsedRulesCount++;
                    }
                    else
                    {
                        warnings.Add($"Neplatná syntax pre RESERVE: '{line}'.");
                    }
                    break;

                case "MAX":
                    // MAX <count> <feature> [GROUP|USER|HOST] <target>
                    if (parts.Length >= 5 && int.TryParse(parts[1], CultureInfo.InvariantCulture, out int maxSeats))
                    {
                        string feat = parts[2].ToUpperInvariant();
                        string targetType = parts[3].ToUpperInvariant();
                        string targetName = parts[4];

                        allocations.Add(new TranspiledSeatAllocation(
                            FeatureCode: feat,
                            TargetType: NormalizeTargetType(targetType),
                            TargetName: targetName,
                            AllocationType: "Limit",
                            Seats: maxSeats));

                        recognized.Add("MAX");
                        parsedRulesCount++;
                    }
                    else
                    {
                        warnings.Add($"Neplatná syntax pre MAX: '{line}'.");
                    }
                    break;

                case "INCLUDE":
                case "INCLUDEALL":
                    // INCLUDE <feature> [GROUP|USER|HOST] <target>
                    if (parts.Length >= 4)
                    {
                        string feat = directive == "INCLUDEALL" ? "*" : parts[1].ToUpperInvariant();
                        string targetType = parts[2].ToUpperInvariant();
                        string targetName = parts[3];

                        entitlements.Add(new TranspiledEntitlement(
                            FeatureCode: feat,
                            TargetType: NormalizeTargetType(targetType),
                            TargetName: targetName,
                            Effect: "Include"));

                        recognized.Add(directive);
                        parsedRulesCount++;
                    }
                    break;

                case "EXCLUDE":
                case "EXCLUDEALL":
                    // EXCLUDE <feature> [GROUP|USER|HOST] <target>
                    if (parts.Length >= 4)
                    {
                        string feat = directive == "EXCLUDEALL" ? "*" : parts[1].ToUpperInvariant();
                        string targetType = parts[2].ToUpperInvariant();
                        string targetName = parts[3];

                        entitlements.Add(new TranspiledEntitlement(
                            FeatureCode: feat,
                            TargetType: NormalizeTargetType(targetType),
                            TargetName: targetName,
                            Effect: "Exclude"));

                        recognized.Add(directive);
                        parsedRulesCount++;
                    }
                    break;

                case "BORROW_LOWWATER":
                    // BORROW_LOWWATER <feature> <min_seats>
                    if (parts.Length >= 3 && int.TryParse(parts[2], CultureInfo.InvariantCulture, out int minB))
                    {
                        borrowMinSeats = Math.Max(borrowMinSeats, minB);
                        borrowingEnabled = true;
                        recognized.Add("BORROW_LOWWATER");
                        parsedRulesCount++;
                    }
                    break;

                case "MAX_BORROW_HOURS":
                    // MAX_BORROW_HOURS <feature> <hours>
                    if (parts.Length >= 3 && int.TryParse(parts[2], CultureInfo.InvariantCulture, out int hours))
                    {
                        maxBorrowDays = Math.Max(1, hours / 24);
                        borrowingEnabled = true;
                        recognized.Add("MAX_BORROW_HOURS");
                        parsedRulesCount++;
                    }
                    break;

                case "TIMEOUT":
                    // TIMEOUT <feature> <seconds>
                    if (parts.Length >= 3 && int.TryParse(parts[2], CultureInfo.InvariantCulture, out int tSec))
                    {
                        featureTimeouts[parts[1].ToUpperInvariant()] = tSec;
                        recognized.Add("TIMEOUT");
                        parsedRulesCount++;
                    }
                    break;

                case "TIMEOUTALL":
                    // TIMEOUTALL <seconds>
                    if (parts.Length >= 2 && int.TryParse(parts[1], CultureInfo.InvariantCulture, out int tAll))
                    {
                        globalTimeout = tAll;
                        recognized.Add("TIMEOUTALL");
                        parsedRulesCount++;
                    }
                    break;

                case "LINGER":
                    // LINGER <feature> <seconds>
                    if (parts.Length >= 3 && int.TryParse(parts[2], CultureInfo.InvariantCulture, out int lingSec))
                    {
                        linger = lingSec;
                        recognized.Add("LINGER");
                        parsedRulesCount++;
                    }
                    break;

                case "REPORTLOG":
                    // FlexNet legacy audit logging
                    recognized.Add("REPORTLOG");
                    recommendations.Add("Smernica REPORTLOG detegovaná: V Symbolone sa audit vykonáva natívne pomocou kryptografického SHA-256 Audit Ledgeru s W3C TraceContext a OpenTelemetry exportom.");
                    break;

                default:
                    warnings.Add($"Nerozpoznaná alebo nepodporovaná FlexNet smernica '{directive}' na riadku: '{line}'.");
                    break;
            }
        }

        var userGroupList = userGroups.Select(kv => new TranspiledGroup(kv.Key, kv.Value.ToList())).ToList();
        var hostGroupList = hostGroups.Select(kv => new TranspiledGroup(kv.Key, kv.Value.ToList())).ToList();
        var borrowing = new TranspiledBorrowingPolicy(borrowingEnabled, maxBorrowDays, borrowMinSeats);

        var policy = new TranspiledPolicyModel(
            UserGroups: userGroupList,
            HostGroups: hostGroupList,
            SeatAllocations: allocations,
            Entitlements: entitlements,
            Borrowing: borrowing,
            IdleTimeoutSeconds: globalTimeout,
            LingerSeconds: linger,
            FeatureTimeouts: featureTimeouts);

        if (allocations.Count > 0)
        {
            recommendations.Add($"Úspešne prevedených {allocations.Count} alokácií sedadiel (RESERVE/MAX). V Symbolone sú garantované bez možnosti race condition vďaka databázovému motoru.");
        }

        if (borrowingEnabled)
        {
            recommendations.Add($"Povolené offline výpožičky (Borrowing) s limitom {maxBorrowDays} dní a ochranou minimálne {borrowMinSeats} dostupných sedadiel pre online stanice.");
        }

        return new OptionsTranspilationReport(
            Policy: policy,
            TotalRulesParsed: parsedRulesCount,
            RecognizedDirectives: recognized.ToList(),
            Warnings: warnings,
            Recommendations: recommendations);
    }

    private static string NormalizeTargetType(string raw) => raw.ToUpperInvariant() switch
    {
        "GROUP" => "Group",
        "HOST_GROUP" => "HostGroup",
        "USER" => "User",
        "HOST" => "Host",
        "INTERNET" => "Subnet",
        _ => "Group"
    };
}
