using System.Collections.Concurrent;
using Symbolon.Domain.PolicyRules;

namespace Symbolon.Relay;

internal sealed class RelayOptionsManager
{
    private readonly ConcurrentDictionary<string, PolicyRuleSet> _rulesByLicense = new(StringComparer.OrdinalIgnoreCase);
    private PolicyRuleSet? _globalRuleSet;

    public RelayOptionsManager(string? optionsFilePath = null)
    {
        string path = optionsFilePath ?? "options.yaml";
        if (File.Exists(path))
        {
            try
            {
                string content = File.ReadAllText(path);
                var ruleSet = PolicyRuleSerializer.Parse(content);
                if (!string.IsNullOrWhiteSpace(ruleSet.LicenseId))
                {
                    _rulesByLicense[ruleSet.LicenseId] = ruleSet;
                }
                else
                {
                    _globalRuleSet = ruleSet;
                }
            }
            catch (Exception ex) when (ex is FormatException or IOException or ArgumentException)
            {
                // Log or ignore corrupted local options file at startup
            }
        }
    }

    public void SetRules(string? licenseId, PolicyRuleSet ruleSet)
    {
        ArgumentNullException.ThrowIfNull(ruleSet);
        if (!string.IsNullOrWhiteSpace(licenseId))
        {
            _rulesByLicense[licenseId] = ruleSet;
        }
        else
        {
            _globalRuleSet = ruleSet;
        }
    }

    public PolicyRuleSet? GetRules(string licenseId)
    {
        if (_rulesByLicense.TryGetValue(licenseId, out var rules))
        {
            return rules;
        }
        return _globalRuleSet;
    }
}
