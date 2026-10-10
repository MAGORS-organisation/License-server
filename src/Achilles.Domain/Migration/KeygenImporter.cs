using System.Text.Json;

namespace Achilles.Domain.Migration;

public sealed record KeygenMappedPolicy(
    string Id,
    string Name,
    string Code,
    int DurationDays,
    int MaxSeats,
    string LicenseModel);

public sealed record KeygenMappedLicense(
    string Id,
    string Key,
    string Name,
    string PolicyId,
    string? UserId,
    int MaxSeats,
    string Status,
    DateTimeOffset? ExpiresAt);

public sealed record KeygenMappedUser(
    string Id,
    string Email,
    string FullName);

public sealed record KeygenImportSummary(
    int TotalRecordsRead,
    IReadOnlyList<KeygenMappedPolicy> Policies,
    IReadOnlyList<KeygenMappedLicense> Licenses,
    IReadOnlyList<KeygenMappedUser> Users,
    IReadOnlyList<string> Warnings);

public static class KeygenImporter
{
    public static KeygenImportSummary Parse(string jsonContent)
    {
        ArgumentNullException.ThrowIfNull(jsonContent);

        var policies = new List<KeygenMappedPolicy>();
        var licenses = new List<KeygenMappedLicense>();
        var users = new List<KeygenMappedUser>();
        var warnings = new List<string>();
        int totalRecords = 0;

        using var doc = JsonDocument.Parse(jsonContent);
        var root = doc.RootElement;

        // Check if format is { "data": [ ... ] } or array [ ... ]
        JsonElement dataArray;
        if (root.ValueKind == JsonValueKind.Array)
        {
            dataArray = root;
        }
        else if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Array)
        {
            dataArray = d;
        }
        else
        {
            warnings.Add("Neznáma štruktúra Keygen JSON exportu. Očakáva sa pole objektov 'data'.");
            return new KeygenImportSummary(0, policies, licenses, users, warnings);
        }

        foreach (var item in dataArray.EnumerateArray())
        {
            totalRecords++;
            if (!item.TryGetProperty("type", out var typeEl) || !item.TryGetProperty("id", out var idEl))
            {
                continue;
            }

            string type = typeEl.GetString()?.ToUpperInvariant() ?? "";
            string id = idEl.GetString() ?? Guid.NewGuid().ToString("N");
            var attrs = item.TryGetProperty("attributes", out var a) ? a : default;

            switch (type)
            {
                case "POLICIES":
                    ParsePolicy(id, attrs, policies);
                    break;

                case "LICENSES":
                    ParseLicense(id, item, attrs, licenses);
                    break;

                case "USERS":
                    ParseUser(id, attrs, users);
                    break;

                default:
                    // machines, tokens, entitlements...
                    break;
            }
        }

        return new KeygenImportSummary(totalRecords, policies, licenses, users, warnings);
    }

    private static void ParsePolicy(string id, JsonElement attrs, List<KeygenMappedPolicy> policies)
    {
        string name = attrs.TryGetProperty("name", out var n) ? (n.GetString() ?? "Policy") : "Policy";
        string code = attrs.TryGetProperty("code", out var c) ? (c.GetString() ?? $"POL-{id[..Math.Min(8, id.Length)]}") : $"POL-{id[..Math.Min(8, id.Length)]}";

        int durationDays = 365;
        if (attrs.TryGetProperty("duration", out var durEl) && durEl.ValueKind == JsonValueKind.Number)
        {
            long seconds = durEl.GetInt64();
            durationDays = seconds > 0 ? (int)(seconds / 86400) : 0;
        }

        int maxSeats = 1;
        if (attrs.TryGetProperty("maxMachines", out var mmEl) && mmEl.ValueKind == JsonValueKind.Number)
        {
            maxSeats = Math.Max(1, mmEl.GetInt32());
        }

        bool isFloating = attrs.TryGetProperty("floating", out var flEl) && flEl.GetBoolean();
        bool isConcurrent = attrs.TryGetProperty("concurrent", out var ccEl) && ccEl.GetBoolean();
        string model = (isFloating || isConcurrent) ? "floating" : "node-lock";

        policies.Add(new KeygenMappedPolicy(id, name, code.ToUpperInvariant(), durationDays, maxSeats, model));
    }

    private static void ParseLicense(string id, JsonElement item, JsonElement attrs, List<KeygenMappedLicense> licenses)
    {
        string key = attrs.TryGetProperty("key", out var k) ? (k.GetString() ?? id) : id;
        string name = attrs.TryGetProperty("name", out var n) ? (n.GetString() ?? "Imported License") : "Imported License";
        string status = attrs.TryGetProperty("status", out var s) ? (s.GetString()?.ToUpperInvariant() ?? "ACTIVE") : "ACTIVE";

        DateTimeOffset? expiresAt = null;
        if (attrs.TryGetProperty("expiry", out var expEl) && expEl.ValueKind == JsonValueKind.String)
        {
            if (DateTimeOffset.TryParse(expEl.GetString(), out var parsedExp))
            {
                expiresAt = parsedExp;
            }
        }

        int maxSeats = 1;
        if (attrs.TryGetProperty("maxMachines", out var mmEl) && mmEl.ValueKind == JsonValueKind.Number)
        {
            maxSeats = Math.Max(1, mmEl.GetInt32());
        }

        string policyId = "default";
        string? userId = null;

        if (item.TryGetProperty("relationships", out var rel))
        {
            if (rel.TryGetProperty("policy", out var polRel) && polRel.TryGetProperty("data", out var polData) && polData.TryGetProperty("id", out var pid))
            {
                policyId = pid.GetString() ?? "default";
            }
            if (rel.TryGetProperty("user", out var usrRel) && usrRel.TryGetProperty("data", out var usrData) && usrData.TryGetProperty("id", out var uid))
            {
                userId = uid.GetString();
            }
        }

        licenses.Add(new KeygenMappedLicense(
            Id: id,
            Key: key,
            Name: name,
            PolicyId: policyId,
            UserId: userId,
            MaxSeats: maxSeats,
            Status: status,
            ExpiresAt: expiresAt));
    }

    private static void ParseUser(string id, JsonElement attrs, List<KeygenMappedUser> users)
    {
        string email = attrs.TryGetProperty("email", out var e) ? (e.GetString() ?? $"user-{id}@imported.local") : $"user-{id}@imported.local";
        string fn = attrs.TryGetProperty("firstName", out var f) ? (f.GetString() ?? "") : "";
        string ln = attrs.TryGetProperty("lastName", out var l) ? (l.GetString() ?? "") : "";
        string fullName = $"{fn} {ln}".Trim();
        if (string.IsNullOrWhiteSpace(fullName)) fullName = email;

        users.Add(new KeygenMappedUser(id, email, fullName));
    }
}
