using System.Globalization;
using System.Text.RegularExpressions;

namespace Achilles.Domain.Migration;

public sealed record FlexNetServerModel(string Hostname, string HostId, int? Port);

public sealed record FlexNetFeatureModel(
    string Name,
    string Vendor,
    string Version,
    DateTimeOffset? ExpirationDate,
    bool IsPermanent,
    int Seats,
    bool IsUncounted,
    string? HostId,
    string? Notice,
    string? SerialNumber,
    string? Sign,
    IReadOnlyDictionary<string, string> AdditionalAttributes);

public sealed record FlexNetPackageModel(
    string Name,
    string Vendor,
    string Version,
    IReadOnlyList<string> Components);

public sealed record FlexNetLicenseFileModel(
    IReadOnlyList<FlexNetServerModel> Servers,
    IReadOnlyList<string> Vendors,
    IReadOnlyList<FlexNetFeatureModel> Features,
    IReadOnlyList<FlexNetPackageModel> Packages,
    IReadOnlyList<string> ParseWarnings);

public sealed record ConvertedProductPlan(
    string Code,
    string Name,
    string Version,
    int TotalSeats,
    bool IsPermanent,
    DateTimeOffset? ExpiresAt,
    string? NodeLockHostId,
    IReadOnlyList<string> Components);

public sealed record FlexNetConversionResult(
    FlexNetLicenseFileModel ParsedFile,
    IReadOnlyList<ConvertedProductPlan> ConvertedPlans,
    IReadOnlyList<string> Recommendations);

public static class FlexNetLicenseParser
{
    private static readonly string[] DateFormats =
    [
        "d-MMM-yyyy",
        "dd-MMM-yyyy",
        "d-MMM-yy",
        "dd-MMM-yy",
        "yyyy-MM-dd",
        "d-MM-yyyy",
        "dd-MM-yyyy"
    ];

    public static FlexNetLicenseFileModel Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var servers = new List<FlexNetServerModel>();
        var vendors = new List<string>();
        var features = new List<FlexNetFeatureModel>();
        var packages = new List<FlexNetPackageModel>();
        var warnings = new List<string>();

        // FlexNet supports line continuations using trailing backslash '\'
        var normalizedLines = NormalizeContinuationLines(content);

        foreach (var rawLine in normalizedLines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith('#') || line.StartsWith("REM", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var tokens = TokenizeLine(line);
            if (tokens.Count == 0) continue;

            string keyword = tokens[0].ToUpperInvariant();

            switch (keyword)
            {
                case "SERVER":
                    ParseServerLine(tokens, servers, warnings);
                    break;

                case "DAEMON":
                case "VENDOR":
                    if (tokens.Count > 1)
                    {
                        vendors.Add(tokens[1]);
                    }
                    break;

                case "FEATURE":
                case "INCREMENT":
                case "UPGRADE":
                    ParseFeatureLine(tokens, features, warnings);
                    break;

                case "PACKAGE":
                    ParsePackageLine(tokens, packages, warnings);
                    break;

                default:
                    // Other directives: USE_SERVER, PACKAGE, etc.
                    break;
            }
        }

        return new FlexNetLicenseFileModel(servers, vendors, features, packages, warnings);
    }

    public static FlexNetConversionResult ConvertToSymbolonPlans(FlexNetLicenseFileModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var plans = new List<ConvertedProductPlan>();
        var recommendations = new List<string>();

        // Map packages first for component expansion
        var packageMap = model.Packages.ToDictionary(p => p.Name.ToUpperInvariant(), p => p.Components);

        foreach (var feat in model.Features)
        {
            IReadOnlyList<string> components = Array.Empty<string>();
            if (packageMap.TryGetValue(feat.Name.ToUpperInvariant(), out var comps))
            {
                components = comps;
            }

            plans.Add(new ConvertedProductPlan(
                Code: feat.Name.ToUpperInvariant(),
                Name: $"{feat.Name} (v{feat.Version})",
                Version: feat.Version,
                TotalSeats: feat.IsUncounted ? 999999 : feat.Seats,
                IsPermanent: feat.IsPermanent,
                ExpiresAt: feat.ExpirationDate,
                NodeLockHostId: feat.HostId,
                Components: components));
        }

        // Generate recommendations
        if (model.Servers.Count > 1)
        {
            recommendations.Add($"Zistená FlexNet Triad konfigurácia ({model.Servers.Count} serverov). V Symbolone nahraďte krehké Triad quórum za vysokodostupnú databázu PostgreSQL 17 HA s lokálnymi offline uzlami Achilles.Relay.");
        }

        foreach (var f in model.Features)
        {
            if (f.IsPermanent)
            {
                recommendations.Add($"Funkcia '{f.Name}' je trvalá (permanent). Odporúča sa v Symbolone nastaviť 'Perpetual' licenčný model s voliteľnou ročnou revíziou údržby (Maintenance Window).");
            }

            if (!string.IsNullOrWhiteSpace(f.HostId))
            {
                recommendations.Add($"Funkcia '{f.Name}' má zadané HostID obmedzenie ({f.HostId}). Symbolon toto prevedie na hardvérový fingerprint stanice s toleranciou výmeny jedného komponentu.");
            }
        }

        return new FlexNetConversionResult(model, plans, recommendations);
    }

    private static List<string> NormalizeContinuationLines(string content)
    {
        var lines = new List<string>();
        using var reader = new StringReader(content);
        string? current = null;

        while (reader.ReadLine() is { } line)
        {
            var trimmed = line.Trim();
            if (trimmed.EndsWith('\\'))
            {
                var withoutBackslash = trimmed[..^1].Trim();
                current = current is null ? withoutBackslash : $"{current} {withoutBackslash}";
            }
            else
            {
                if (current is not null)
                {
                    lines.Add($"{current} {trimmed}".Trim());
                    current = null;
                }
                else
                {
                    lines.Add(trimmed);
                }
            }
        }

        if (current is not null)
        {
            lines.Add(current);
        }

        return lines;
    }

    private static void ParseServerLine(List<string> tokens, List<FlexNetServerModel> servers, List<string> warnings)
    {
        // SERVER <hostname> <hostid> [<port>]
        if (tokens.Count < 3)
        {
            warnings.Add($"Neúplný SERVER záznam: '{string.Join(" ", tokens)}'.");
            return;
        }

        string hostname = tokens[1];
        string hostId = tokens[2];
        int? port = null;

        if (tokens.Count > 3 && int.TryParse(tokens[3], CultureInfo.InvariantCulture, out int parsedPort))
        {
            port = parsedPort;
        }

        servers.Add(new FlexNetServerModel(hostname, hostId, port));
    }

    private static void ParseFeatureLine(List<string> tokens, List<FlexNetFeatureModel> features, List<string> warnings)
    {
        // FEATURE/INCREMENT <name> <vendor> <version> <exp_date> <seats> [attribs...]
        if (tokens.Count < 6)
        {
            warnings.Add($"Neúplný FEATURE/INCREMENT záznam: '{string.Join(" ", tokens)}'.");
            return;
        }

        string name = tokens[1];
        string vendor = tokens[2];
        string version = tokens[3];
        string expStr = tokens[4];
        string seatsStr = tokens[5];

        bool isPermanent = false;
        DateTimeOffset? expDate = null;

        if (string.Equals(expStr, "permanent", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(expStr, "0", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(expStr, "none", StringComparison.OrdinalIgnoreCase))
        {
            isPermanent = true;
        }
        else
        {
            if (DateTimeOffset.TryParseExact(expStr, DateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsedDate))
            {
                expDate = parsedDate;
            }
            else
            {
                warnings.Add($"Neznámy formát dátumu expirácie '{expStr}' pre funkciu '{name}'.");
            }
        }

        bool isUncounted = false;
        int seats = 1;

        if (string.Equals(seatsStr, "uncounted", StringComparison.OrdinalIgnoreCase))
        {
            isUncounted = true;
            seats = 0;
        }
        else if (int.TryParse(seatsStr, CultureInfo.InvariantCulture, out int parsedSeats))
        {
            seats = Math.Max(0, parsedSeats);
            if (seats == 0) isUncounted = true;
        }

        string? hostId = null;
        string? notice = null;
        string? sn = null;
        string? sign = null;
        var additional = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 6; i < tokens.Count; i++)
        {
            var token = tokens[i];
            int eqIdx = token.IndexOf('=', StringComparison.Ordinal);
            if (eqIdx > 0)
            {
                string key = token[..eqIdx].ToUpperInvariant();
                string val = token[(eqIdx + 1)..].Trim('"');

                switch (key)
                {
                    case "HOSTID":
                        hostId = val;
                        break;
                    case "NOTICE":
                        notice = val;
                        break;
                    case "SN":
                        sn = val;
                        break;
                    case "SIGN":
                    case "SIGN2":
                    case "AUTH":
                        sign = val;
                        break;
                    default:
                        additional[key] = val;
                        break;
                }
            }
        }

        features.Add(new FlexNetFeatureModel(
            Name: name,
            Vendor: vendor,
            Version: version,
            ExpirationDate: expDate,
            IsPermanent: isPermanent,
            Seats: seats,
            IsUncounted: isUncounted,
            HostId: hostId,
            Notice: notice,
            SerialNumber: sn,
            Sign: sign,
            AdditionalAttributes: additional));
    }

    private static void ParsePackageLine(List<string> tokens, List<FlexNetPackageModel> packages, List<string> warnings)
    {
        // PACKAGE <name> <vendor> <version> COMPONENTS="comp1:1.0 comp2:1.0"
        if (tokens.Count < 4)
        {
            warnings.Add($"Neúplný PACKAGE záznam: '{string.Join(" ", tokens)}'.");
            return;
        }

        string name = tokens[1];
        string vendor = tokens[2];
        string version = tokens[3];
        var components = new List<string>();

        for (int i = 4; i < tokens.Count; i++)
        {
            var token = tokens[i];
            if (token.StartsWith("COMPONENTS=", StringComparison.OrdinalIgnoreCase))
            {
                string raw = token["COMPONENTS=".Length..].Trim('"');
                var parts = raw.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    int colonIdx = part.IndexOf(':', StringComparison.Ordinal);
                    components.Add(colonIdx > 0 ? part[..colonIdx] : part);
                }
            }
        }

        packages.Add(new FlexNetPackageModel(name, vendor, version, components));
    }

    private static List<string> TokenizeLine(string line)
    {
        var tokens = new List<string>();
        var matches = Regex.Matches(line, """[^\s="]+="[^"]*"|"[^"]*"|[^\s]+""", RegexOptions.None, TimeSpan.FromMilliseconds(500));
        foreach (Match match in matches)
        {
            tokens.Add(match.Value);
        }
        return tokens;
    }
}
