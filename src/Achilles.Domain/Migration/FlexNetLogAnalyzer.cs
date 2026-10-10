using System.Globalization;
using System.Text.RegularExpressions;

namespace Achilles.Domain.Migration;

public sealed record FeatureUsageStats(
    string FeatureCode,
    int PeakConcurrency,
    int TotalCheckouts,
    int TotalCheckins,
    int TotalDenials,
    int UniqueUsersCount,
    int UniqueHostsCount,
    IReadOnlyList<string> TopDenialReasons,
    int RecommendedSeats,
    int RecommendedOverdraftBuffer,
    string SizingRationale);

public sealed record HourlyConcurrency(int HourOfDay, int PeakConcurrency, int CheckoutCount);

public sealed record LogAnalysisResult(
    int TotalLinesAnalyzed,
    int TotalEventsCount,
    IReadOnlyList<FeatureUsageStats> FeatureStatistics,
    IReadOnlyList<HourlyConcurrency> HourlyDistribution,
    int OverallPeakConcurrency,
    int TotalDenialsAcrossAllFeatures,
    IReadOnlyList<string> KeyInsights);

public static class FlexNetLogAnalyzer
{
    // Regex for: HH:mm:ss (vendor) ACTION: "FEATURE" user@host ...
    private static readonly Regex LogRegex = new(
        @"^(?<time>\d{1,2}:\d{2}:\d{2})\s+\((?<vendor>[^)]+)\)\s+(?<action>OUT|IN|DENIED|UNSUPPORTED):\s+""?(?<feature>[^""\s]+)""?\s+(?<target>[^\s(]+)?(?:\s+\((?<reason>[^)]+)\))?",
        RegexOptions.Compiled | RegexOptions.IgnoreCase,
        TimeSpan.FromMilliseconds(500));

    public static LogAnalysisResult Analyze(string logContent)
    {
        ArgumentNullException.ThrowIfNull(logContent);

        int totalLines = 0;
        int totalEvents = 0;

        // Track active leases per feature: feature -> set of "user@host"
        var activeLeases = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var peakMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var checkoutsMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var checkinsMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var denialsMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var denialReasons = new Dictionary<string, Dictionary<string, int>>(StringComparer.OrdinalIgnoreCase);
        var uniqueUsers = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var uniqueHosts = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        // Hourly stats: 0-23
        var hourlyPeak = new int[24];
        var hourlyCheckouts = new int[24];

        using var reader = new StringReader(logContent);
        string? line;

        while ((line = reader.ReadLine()) is not null)
        {
            totalLines++;
            var match = LogRegex.Match(line);
            if (!match.Success) continue;

            totalEvents++;
            string timeStr = match.Groups["time"].Value;
            string action = match.Groups["action"].Value.ToUpperInvariant();
            string feature = match.Groups["feature"].Value.ToUpperInvariant();
            string target = match.Groups["target"].Success ? match.Groups["target"].Value : "unknown";
            string reason = match.Groups["reason"].Success ? match.Groups["reason"].Value : "Licensed limit reached";

            int hour = 0;
            if (timeStr.Length >= 2 && int.TryParse(timeStr[..2], CultureInfo.InvariantCulture, out int h) && h is >= 0 and <= 23)
            {
                hour = h;
            }

            if (!activeLeases.TryGetValue(feature, out var currentActive))
            {
                currentActive = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                activeLeases[feature] = currentActive;
            }

            // Extract user and host from "user@host"
            string user = target;
            string host = target;
            int atIdx = target.IndexOf('@', StringComparison.Ordinal);
            if (atIdx >= 0)
            {
                user = target[..atIdx];
                host = target[(atIdx + 1)..];
            }

            if (!uniqueUsers.TryGetValue(feature, out var usersSet))
            {
                usersSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                uniqueUsers[feature] = usersSet;
            }
            usersSet.Add(user);

            if (!uniqueHosts.TryGetValue(feature, out var hostsSet))
            {
                hostsSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                uniqueHosts[feature] = hostsSet;
            }
            hostsSet.Add(host);

            switch (action)
            {
                case "OUT":
                    currentActive.Add(target);
                    checkoutsMap[feature] = checkoutsMap.GetValueOrDefault(feature) + 1;
                    hourlyCheckouts[hour]++;

                    int currentCount = currentActive.Count;
                    if (currentCount > peakMap.GetValueOrDefault(feature))
                    {
                        peakMap[feature] = currentCount;
                    }

                    int totalActiveNow = activeLeases.Values.Sum(s => s.Count);
                    if (totalActiveNow > hourlyPeak[hour])
                    {
                        hourlyPeak[hour] = totalActiveNow;
                    }
                    break;

                case "IN":
                    currentActive.Remove(target);
                    checkinsMap[feature] = checkinsMap.GetValueOrDefault(feature) + 1;
                    break;

                case "DENIED":
                    denialsMap[feature] = denialsMap.GetValueOrDefault(feature) + 1;
                    if (!denialReasons.TryGetValue(feature, out var reasonsDict))
                    {
                        reasonsDict = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                        denialReasons[feature] = reasonsDict;
                    }
                    reasonsDict[reason] = reasonsDict.GetValueOrDefault(reason) + 1;
                    break;
            }
        }

        // Build feature statistics & right-sizing recommendations
        var featureStatsList = new List<FeatureUsageStats>();
        var allFeatures = peakMap.Keys.Union(checkoutsMap.Keys).Union(denialsMap.Keys).OrderBy(f => f).ToList();

        foreach (var feat in allFeatures)
        {
            int peak = peakMap.GetValueOrDefault(feat);
            int outCount = checkoutsMap.GetValueOrDefault(feat);
            int inCount = checkinsMap.GetValueOrDefault(feat);
            int denCount = denialsMap.GetValueOrDefault(feat);
            int uUsers = uniqueUsers.TryGetValue(feat, out var u) ? u.Count : 0;
            int uHosts = uniqueHosts.TryGetValue(feat, out var h) ? h.Count : 0;

            IReadOnlyList<string> topReasons = denialReasons.TryGetValue(feat, out var rDict)
                ? rDict.OrderByDescending(kv => kv.Value).Take(3).Select(kv => $"{kv.Key} ({kv.Value.ToString(CultureInfo.InvariantCulture)}x)").ToList()
                : Array.Empty<string>();

            // Right-sizing calculation:
            // Recommended seats = Peak (or slightly padded if denials occurred)
            int recSeats;
            int recBuffer;
            string rationale;

            if (denCount > 0)
            {
                // Under-provisioned in legacy FlexNet
                recSeats = Math.Max(1, peak + Math.Min(10, (int)Math.Ceiling(denCount * 0.15)));
                recBuffer = Math.Max(2, (int)Math.Ceiling(recSeats * 0.20));
                rationale = $"Zistených {denCount} odmietnutí (DENIED) v histórii. Odporúča sa navýšiť základné sedadlá na {recSeats} a aktivovať tokenový overdraft buffer +{recBuffer} kreditov pre špičky.";
            }
            else if (peak > 0)
            {
                recSeats = peak;
                recBuffer = Math.Max(1, (int)Math.Ceiling(peak * 0.10));
                rationale = $"Žiadne odmietnutia v histórii. Špička dosiahla {peak} súbežných staníc. Odporúčaná alokácia: {recSeats} sedadiel + {recBuffer} token buffer.";
            }
            else
            {
                recSeats = 1;
                recBuffer = 1;
                rationale = "Minimálna alokácia na základe nízkej evidovanej aktivity.";
            }

            featureStatsList.Add(new FeatureUsageStats(
                FeatureCode: feat,
                PeakConcurrency: peak,
                TotalCheckouts: outCount,
                TotalCheckins: inCount,
                TotalDenials: denCount,
                UniqueUsersCount: uUsers,
                UniqueHostsCount: uHosts,
                TopDenialReasons: topReasons,
                RecommendedSeats: recSeats,
                RecommendedOverdraftBuffer: recBuffer,
                SizingRationale: rationale));
        }

        var hourlyList = new List<HourlyConcurrency>();
        for (int i = 0; i < 24; i++)
        {
            hourlyList.Add(new HourlyConcurrency(i, hourlyPeak[i], hourlyCheckouts[i]));
        }

        int overallPeak = hourlyPeak.Max();
        int totalDenials = denialsMap.Values.Sum();

        var insights = new List<string>();
        if (totalEvents == 0)
        {
            insights.Add("V zadanom súbore neboli nájdené žiadne štandardné FlexNet OUT/IN/DENIED záznamy.");
        }
        else
        {
            insights.Add($"Analyzovaných celkovo {totalEvents} licenčných udalostí naprieč {featureStatsList.Count} funkciami.");
            if (totalDenials > 0)
            {
                insights.Add($"Zistených celkovo {totalDenials} zamietnutých prístupov (DENIED). Prechod na Symbolon s kreditovým Overdraft bufferom úplne eliminuje zablokovanie inžinierov.");
            }
            else
            {
                insights.Add("Historické dáta nevykazujú nedostatok licencií; kapacita môže byť optimalizovaná bez rizika výpadkov.");
            }

            var busiestHour = hourlyList.OrderByDescending(h => h.PeakConcurrency).FirstOrDefault();
            if (busiestHour is not null && busiestHour.PeakConcurrency > 0)
            {
                insights.Add($"Najvyťaženejší čas dňa: {busiestHour.HourOfDay:D2}:00 - {(busiestHour.HourOfDay + 1):D2}:00 (špička {busiestHour.PeakConcurrency} sedadiel).");
            }
        }

        return new LogAnalysisResult(
            TotalLinesAnalyzed: totalLines,
            TotalEventsCount: totalEvents,
            FeatureStatistics: featureStatsList,
            HourlyDistribution: hourlyList,
            OverallPeakConcurrency: overallPeak,
            TotalDenialsAcrossAllFeatures: totalDenials,
            KeyInsights: insights);
    }
}
