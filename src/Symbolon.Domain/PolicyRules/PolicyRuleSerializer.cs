using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Symbolon.Domain.PolicyRules;

public static class PolicyRuleSerializer
{
    public static PolicyRuleSet Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        string trimmed = content.Trim();
        if (trimmed.StartsWith('{'))
        {
            return ParseJson(trimmed);
        }

        return ParseYaml(content);
    }

    public static PolicyRuleSet ParseJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        int version = root.TryGetProperty("version", out var vProp) && vProp.TryGetInt32(out int v) ? v : 1;
        string? licenseId = root.TryGetProperty("license", out var lProp) ? lProp.GetString() : null;

        var groups = new List<PolicyRuleGroup>();
        if (root.TryGetProperty("groups", out var groupsProp) && groupsProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var gElem in groupsProp.EnumerateArray())
            {
                string name = gElem.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                var members = ReadStringList(gElem, "members");
                var hosts = ReadStringList(gElem, "hosts");
                groups.Add(new PolicyRuleGroup(name, members, hosts));
            }
        }

        var rules = new List<PolicyRule>();
        if (root.TryGetProperty("rules", out var rulesProp) && rulesProp.ValueKind == JsonValueKind.Array)
        {
            foreach (var rElem in rulesProp.EnumerateArray())
            {
                string typeStr = rElem.TryGetProperty("type", out var tProp) ? tProp.GetString() ?? string.Empty : string.Empty;
                int? val = rElem.TryGetProperty("value", out var valProp) && valProp.TryGetInt32(out int iv) ? iv : null;

                if (string.IsNullOrEmpty(typeStr))
                {
                    if (rElem.TryGetProperty("deny", out _)) typeStr = "deny";
                    else if (rElem.TryGetProperty("reserve", out var rv)) { typeStr = "reserve"; if (rv.TryGetInt32(out int rvi)) val = rvi; }
                    else if (rElem.TryGetProperty("max", out var mv)) { typeStr = "max"; if (mv.TryGetInt32(out int mvi)) val = mvi; }
                    else if (rElem.TryGetProperty("priority", out var pv)) { typeStr = "priority"; if (pv.TryGetInt32(out int pvi)) val = pvi; }
                }

                var ruleType = ParseRuleType(typeStr);
                RuleTarget? target = null;
                if (rElem.TryGetProperty("for", out var forProp) && forProp.ValueKind == JsonValueKind.Object)
                {
                    target = new RuleTarget(
                        Group: forProp.TryGetProperty("group", out var fg) ? fg.GetString() : null,
                        User: forProp.TryGetProperty("user", out var fu) ? fu.GetString() : null,
                        Subnet: forProp.TryGetProperty("subnet", out var fs) ? fs.GetString() : null,
                        Host: forProp.TryGetProperty("host", out var fh) ? fh.GetString() : null);
                }

                var hosts = ReadStringList(rElem, "hosts");
                var users = ReadStringList(rElem, "users");
                var groupList = ReadStringList(rElem, "groups");
                var subnets = ReadStringList(rElem, "subnets");
                string? feature = rElem.TryGetProperty("feature", out var f) ? f.GetString() : null;
                string? reason = rElem.TryGetProperty("reason", out var r) ? r.GetString() : null;

                rules.Add(new PolicyRule(ruleType, val, target, hosts, users, groupList, subnets, feature, reason));
            }
        }

        return new PolicyRuleSet(version, licenseId, groups, rules);
    }

    private static List<string> ReadStringList(JsonElement parent, string propName)
    {
        if (parent.TryGetProperty(propName, out var prop) && prop.ValueKind == JsonValueKind.Array)
        {
            var list = new List<string>();
            foreach (var item in prop.EnumerateArray())
            {
                if (item.GetString() is { } s && !string.IsNullOrWhiteSpace(s))
                {
                    list.Add(s);
                }
            }
            return list;
        }
        return [];
    }

    public static PolicyRuleSet ParseYaml(string yaml)
    {
        ArgumentNullException.ThrowIfNull(yaml);

        int version = 1;
        string? licenseId = null;
        var groups = new List<PolicyRuleGroup>();
        var rules = new List<PolicyRule>();

        string[] lines = yaml.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
        string currentSection = string.Empty;

        // Group parsing state
        string? currentGroupName = null;
        List<string>? currentGroupMembers = null;
        List<string>? currentGroupHosts = null;

        void CommitGroup()
        {
            if (!string.IsNullOrWhiteSpace(currentGroupName))
            {
                groups.Add(new PolicyRuleGroup(
                    currentGroupName,
                    currentGroupMembers ?? (IReadOnlyList<string>)Array.Empty<string>(),
                    currentGroupHosts));
            }
            currentGroupName = null;
            currentGroupMembers = null;
            currentGroupHosts = null;
        }

        // Rule parsing state
        PolicyRuleType? currentRuleType = null;
        int? currentRuleValue = null;
        RuleTarget? currentRuleTarget = null;
        List<string>? currentRuleHosts = null;
        List<string>? currentRuleUsers = null;
        List<string>? currentRuleGroups = null;
        List<string>? currentRuleSubnets = null;
        string? currentRuleFeature = null;
        string? currentRuleReason = null;

        void CommitRule()
        {
            if (currentRuleType.HasValue)
            {
                rules.Add(new PolicyRule(
                    currentRuleType.Value,
                    currentRuleValue,
                    currentRuleTarget,
                    currentRuleHosts,
                    currentRuleUsers,
                    currentRuleGroups,
                    currentRuleSubnets,
                    currentRuleFeature,
                    currentRuleReason));
            }
            currentRuleType = null;
            currentRuleValue = null;
            currentRuleTarget = null;
            currentRuleHosts = null;
            currentRuleUsers = null;
            currentRuleGroups = null;
            currentRuleSubnets = null;
            currentRuleFeature = null;
            currentRuleReason = null;
        }

        foreach (string rawLine in lines)
        {
            string line = rawLine.Trim();
            if (line.StartsWith('#') || string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            if (!rawLine.StartsWith(' ') && !rawLine.StartsWith('\t') && line.Contains(':', StringComparison.Ordinal))
            {
                var parts = line.Split(':', 2);
                string key = parts[0].Trim().ToUpperInvariant();
                string val = parts.Length > 1 ? parts[1].Trim() : string.Empty;

                if (key == "VERSION")
                {
                    if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int v))
                    {
                        version = v;
                    }
                    continue;
                }
                if (key == "LICENSE")
                {
                    licenseId = val.Trim('"', '\'');
                    continue;
                }
                if (key == "GROUPS")
                {
                    CommitRule();
                    CommitGroup();
                    currentSection = "groups";
                    continue;
                }
                if (key == "RULES")
                {
                    CommitGroup();
                    CommitRule();
                    currentSection = "rules";
                    continue;
                }
            }

            if (currentSection == "groups")
            {
                if (line.StartsWith("- name:", StringComparison.OrdinalIgnoreCase))
                {
                    CommitGroup();
                    currentGroupName = line["- name:".Length..].Trim().Trim('"', '\'');
                    currentGroupMembers = new List<string>();
                }
                else if (line.StartsWith("name:", StringComparison.OrdinalIgnoreCase))
                {
                    currentGroupName = line["name:".Length..].Trim().Trim('"', '\'');
                }
                else if (line.StartsWith("members:", StringComparison.OrdinalIgnoreCase))
                {
                    string membersVal = line["members:".Length..].Trim();
                    currentGroupMembers = ParseInlineArray(membersVal);
                }
                else if (line.StartsWith("hosts:", StringComparison.OrdinalIgnoreCase))
                {
                    string hostsVal = line["hosts:".Length..].Trim();
                    currentGroupHosts = ParseInlineArray(hostsVal);
                }
            }
            else if (currentSection == "rules")
            {
                if (line.StartsWith("- ", StringComparison.Ordinal))
                {
                    CommitRule();
                    string ruleLine = line[2..].Trim();
                    ProcessRuleProperty(ruleLine, ref currentRuleType, ref currentRuleValue, ref currentRuleTarget,
                        ref currentRuleHosts, ref currentRuleUsers, ref currentRuleGroups, ref currentRuleSubnets,
                        ref currentRuleFeature, ref currentRuleReason);
                }
                else
                {
                    ProcessRuleProperty(line, ref currentRuleType, ref currentRuleValue, ref currentRuleTarget,
                        ref currentRuleHosts, ref currentRuleUsers, ref currentRuleGroups, ref currentRuleSubnets,
                        ref currentRuleFeature, ref currentRuleReason);
                }
            }
        }

        CommitGroup();
        CommitRule();

        return new PolicyRuleSet(version, licenseId, groups, rules);
    }

    private static void ProcessRuleProperty(
        string line,
        ref PolicyRuleType? ruleType,
        ref int? ruleValue,
        ref RuleTarget? target,
        ref List<string>? hosts,
        ref List<string>? users,
        ref List<string>? groups,
        ref List<string>? subnets,
        ref string? feature,
        ref string? reason)
    {
        int colonIdx = line.IndexOf(':', StringComparison.Ordinal);
        if (colonIdx < 0) return;

        string key = line[..colonIdx].Trim().ToUpperInvariant();
        string val = line[(colonIdx + 1)..].Trim();

        switch (key)
        {
            case "TYPE":
                ruleType = ParseRuleType(val.Trim('"', '\''));
                break;
            case "VALUE":
            case "QUANTITY":
                if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int vVal))
                    ruleValue = vVal;
                break;
            case "GROUP":
                target = (target ?? new RuleTarget()) with { Group = val.Trim('"', '\'') };
                break;
            case "USER":
                target = (target ?? new RuleTarget()) with { User = val.Trim('"', '\'') };
                break;
            case "HOST":
                target = (target ?? new RuleTarget()) with { Host = val.Trim('"', '\'') };
                break;
            case "SUBNET":
                target = (target ?? new RuleTarget()) with { Subnet = val.Trim('"', '\'') };
                break;
            case "TARGETNAME":
            case "TARGET":
                string unquotedTarget = val.Trim('"', '\'');
                if (unquotedTarget.StartsWith("group:", StringComparison.OrdinalIgnoreCase))
                    target = (target ?? new RuleTarget()) with { Group = unquotedTarget["group:".Length..].Trim() };
                else if (unquotedTarget.StartsWith("user:", StringComparison.OrdinalIgnoreCase))
                    target = (target ?? new RuleTarget()) with { User = unquotedTarget["user:".Length..].Trim() };
                else
                    target = (target ?? new RuleTarget()) with { Group = unquotedTarget };
                break;
            case "DENY":
                ruleType = PolicyRuleType.Deny;
                break;
            case "RESERVE":
                ruleType = PolicyRuleType.Reserve;
                if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int rVal))
                    ruleValue = rVal;
                break;
            case "MAX":
                ruleType = PolicyRuleType.Max;
                if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int mVal))
                    ruleValue = mVal;
                break;
            case "PRIORITY":
                ruleType = PolicyRuleType.Priority;
                if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out int pVal))
                    ruleValue = pVal;
                break;
            case "FEATURE":
                feature = val.Trim('"', '\'');
                break;
            case "REASON":
            case "DESCRIPTION":
                reason = val.Trim('"', '\'');
                break;
            case "HOSTS":
                hosts = ParseInlineArray(val);
                break;
            case "USERS":
                users = ParseInlineArray(val);
                break;
            case "GROUPS":
                groups = ParseInlineArray(val);
                break;
            case "SUBNETS":
                subnets = ParseInlineArray(val);
                break;
            case "FOR":
                target = ParseTarget(val);
                break;
        }
    }

    private static RuleTarget ParseTarget(string targetVal)
    {
        string trimmed = targetVal.Trim();
        if (trimmed.StartsWith('{') && trimmed.EndsWith('}'))
        {
            trimmed = trimmed[1..^1];
        }

        string? group = null;
        string? user = null;
        string? subnet = null;
        string? host = null;

        var tokens = trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var tok in tokens)
        {
            var pair = tok.Split(':', 2);
            if (pair.Length != 2) continue;
            string k = pair[0].Trim().Trim('"', '\'', ' ').ToUpperInvariant();
            string v = pair[1].Trim().Trim('"', '\'', ' ');

            if (k == "GROUP") group = v;
            else if (k == "USER") user = v;
            else if (k == "SUBNET") subnet = v;
            else if (k == "HOST") host = v;
        }

        return new RuleTarget(group, user, subnet, host);
    }

    private static List<string> ParseInlineArray(string arrayStr)
    {
        var list = new List<string>();
        string trimmed = arrayStr.Trim();
        if (trimmed.StartsWith('[') && trimmed.EndsWith(']'))
        {
            trimmed = trimmed[1..^1];
        }

        var items = trimmed.Split(',', StringSplitOptions.RemoveEmptyEntries);
        foreach (var item in items)
        {
            string s = item.Trim().Trim('"', '\'');
            if (!string.IsNullOrWhiteSpace(s))
            {
                list.Add(s);
            }
        }
        return list;
    }

    private static PolicyRuleType ParseRuleType(string typeStr) =>
        typeStr.ToUpperInvariant() switch
        {
            "DENY" => PolicyRuleType.Deny,
            "MAX" => PolicyRuleType.Max,
            "RESERVE" => PolicyRuleType.Reserve,
            "PRIORITY" => PolicyRuleType.Priority,
            _ => PolicyRuleType.Deny
        };

    public static string ToYaml(PolicyRuleSet ruleSet)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);

        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"version: {ruleSet.Version}");
        if (!string.IsNullOrWhiteSpace(ruleSet.LicenseId))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"license: {ruleSet.LicenseId}");
        }

        if (ruleSet.Groups.Count > 0)
        {
            sb.AppendLine("groups:");
            foreach (var g in ruleSet.Groups)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"  - name: \"{g.Name}\"");
                if (g.Members.Count > 0)
                {
                    sb.Append("    members: [");
                    sb.Append(string.Join(", ", g.Members.Select(m => $"\"{m}\"")));
                    sb.AppendLine("]");
                }
                if (g.Hosts is { Count: > 0 })
                {
                    sb.Append("    hosts: [");
                    sb.Append(string.Join(", ", g.Hosts.Select(h => $"\"{h}\"")));
                    sb.AppendLine("]");
                }
            }
        }

        if (ruleSet.Rules.Count > 0)
        {
            sb.AppendLine("rules:");
            foreach (var r in ruleSet.Rules)
            {
                switch (r.Type)
                {
                    case PolicyRuleType.Reserve:
                        sb.AppendLine(CultureInfo.InvariantCulture, $"  - reserve: {r.Value ?? 1}");
                        break;
                    case PolicyRuleType.Max:
                        sb.AppendLine(CultureInfo.InvariantCulture, $"  - max: {r.Value ?? 1}");
                        break;
                    case PolicyRuleType.Priority:
                        sb.AppendLine(CultureInfo.InvariantCulture, $"  - priority: {r.Value ?? 1}");
                        break;
                    case PolicyRuleType.Deny:
                        sb.AppendLine("  - deny:");
                        break;
                }

                if (r.Target is { } t)
                {
                    sb.Append("    for: {");
                    var targetPairs = new List<string>();
                    if (!string.IsNullOrEmpty(t.Group)) targetPairs.Add($"group: \"{t.Group}\"");
                    if (!string.IsNullOrEmpty(t.User)) targetPairs.Add($"user: \"{t.User}\"");
                    if (!string.IsNullOrEmpty(t.Subnet)) targetPairs.Add($"subnet: \"{t.Subnet}\"");
                    if (!string.IsNullOrEmpty(t.Host)) targetPairs.Add($"host: \"{t.Host}\"");
                    sb.Append(string.Join(", ", targetPairs));
                    sb.AppendLine("}");
                }

                if (r.Hosts is { Count: > 0 })
                {
                    sb.Append("    hosts: [");
                    sb.Append(string.Join(", ", r.Hosts.Select(h => $"\"{h}\"")));
                    sb.AppendLine("]");
                }
                if (r.Users is { Count: > 0 })
                {
                    sb.Append("    users: [");
                    sb.Append(string.Join(", ", r.Users.Select(u => $"\"{u}\"")));
                    sb.AppendLine("]");
                }
                if (r.Groups is { Count: > 0 })
                {
                    sb.Append("    groups: [");
                    sb.Append(string.Join(", ", r.Groups.Select(g => $"\"{g}\"")));
                    sb.AppendLine("]");
                }
                if (r.Subnets is { Count: > 0 })
                {
                    sb.Append("    subnets: [");
                    sb.Append(string.Join(", ", r.Subnets.Select(s => $"\"{s}\"")));
                    sb.AppendLine("]");
                }

                if (!string.IsNullOrEmpty(r.Feature))
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"    feature: \"{r.Feature}\"");
                }
                if (!string.IsNullOrEmpty(r.Reason))
                {
                    sb.AppendLine(CultureInfo.InvariantCulture, $"    reason: \"{r.Reason}\"");
                }
            }
        }

        return sb.ToString();
    }

    public static string ToJson(PolicyRuleSet ruleSet)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true }))
        {
            writer.WriteStartObject();
            writer.WriteNumber("version", ruleSet.Version);
            if (!string.IsNullOrWhiteSpace(ruleSet.LicenseId))
            {
                writer.WriteString("license", ruleSet.LicenseId);
            }

            writer.WriteStartArray("groups");
            foreach (var g in ruleSet.Groups)
            {
                writer.WriteStartObject();
                writer.WriteString("name", g.Name);
                writer.WriteStartArray("members");
                foreach (var m in g.Members) writer.WriteStringValue(m);
                writer.WriteEndArray();
                if (g.Hosts is { Count: > 0 })
                {
                    writer.WriteStartArray("hosts");
                    foreach (var h in g.Hosts) writer.WriteStringValue(h);
                    writer.WriteEndArray();
                }
                writer.WriteEndObject();
            }
            writer.WriteEndArray();

            writer.WriteStartArray("rules");
            foreach (var r in ruleSet.Rules)
            {
                writer.WriteStartObject();
                string typeStr = r.Type switch
                {
                    PolicyRuleType.Deny => "deny",
                    PolicyRuleType.Max => "max",
                    PolicyRuleType.Reserve => "reserve",
                    PolicyRuleType.Priority => "priority",
                    _ => "deny"
                };
                writer.WriteString("type", typeStr);
                if (r.Value.HasValue) writer.WriteNumber("value", r.Value.Value);
                if (r.Target is { } t)
                {
                    writer.WriteStartObject("for");
                    if (!string.IsNullOrEmpty(t.Group)) writer.WriteString("group", t.Group);
                    if (!string.IsNullOrEmpty(t.User)) writer.WriteString("user", t.User);
                    if (!string.IsNullOrEmpty(t.Subnet)) writer.WriteString("subnet", t.Subnet);
                    if (!string.IsNullOrEmpty(t.Host)) writer.WriteString("host", t.Host);
                    writer.WriteEndObject();
                }
                if (r.Hosts is { Count: > 0 })
                {
                    writer.WriteStartArray("hosts");
                    foreach (var h in r.Hosts) writer.WriteStringValue(h);
                    writer.WriteEndArray();
                }
                if (r.Users is { Count: > 0 })
                {
                    writer.WriteStartArray("users");
                    foreach (var u in r.Users) writer.WriteStringValue(u);
                    writer.WriteEndArray();
                }
                if (r.Groups is { Count: > 0 })
                {
                    writer.WriteStartArray("groups");
                    foreach (var gr in r.Groups) writer.WriteStringValue(gr);
                    writer.WriteEndArray();
                }
                if (r.Subnets is { Count: > 0 })
                {
                    writer.WriteStartArray("subnets");
                    foreach (var s in r.Subnets) writer.WriteStringValue(s);
                    writer.WriteEndArray();
                }
                if (!string.IsNullOrEmpty(r.Feature)) writer.WriteString("feature", r.Feature);
                if (!string.IsNullOrEmpty(r.Reason)) writer.WriteString("reason", r.Reason);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }
}
