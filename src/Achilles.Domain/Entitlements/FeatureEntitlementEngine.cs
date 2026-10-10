using System.Globalization;

namespace Achilles.Domain.Entitlements;

public static class VersionRangeMatcher
{
    public static bool IsVersionAllowed(string? requestedVersion, string? allowedRange)
    {
        if (string.IsNullOrWhiteSpace(allowedRange) || allowedRange == "*")
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(requestedVersion))
        {
            return true;
        }

        string trimmedRequested = requestedVersion.Trim();
        string trimmedRange = allowedRange.Trim();

        if (string.Equals(trimmedRange, trimmedRequested, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(trimmedRange, "latest", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // Wildcard prefix matching, e.g. "2026.*"
        if (trimmedRange.EndsWith(".*", StringComparison.Ordinal))
        {
            string prefix = trimmedRange[..^1]; // includes the trailing dot
            return trimmedRequested.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        // Interval range, e.g. ">= 2025.0 and <= 2027.0" or ">= 2.0"
        if (trimmedRange.Contains(">=", StringComparison.Ordinal) || trimmedRange.Contains("<=", StringComparison.Ordinal))
        {
            return EvaluateInterval(trimmedRequested, trimmedRange);
        }

        return false;
    }

    private static bool EvaluateInterval(string requested, string range)
    {
        var tokens = range.Split(["and", "AND", "&&", ","], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        foreach (var token in tokens)
        {
            if (token.StartsWith(">=", StringComparison.Ordinal))
            {
                string minStr = token[2..].Trim();
                if (CompareVersions(requested, minStr) < 0) return false;
            }
            else if (token.StartsWith('>'))
            {
                string minStr = token[1..].Trim();
                if (CompareVersions(requested, minStr) <= 0) return false;
            }
            else if (token.StartsWith("<=", StringComparison.Ordinal))
            {
                string maxStr = token[2..].Trim();
                if (CompareVersions(requested, maxStr) > 0) return false;
            }
            else if (token.StartsWith('<'))
            {
                string maxStr = token[1..].Trim();
                if (CompareVersions(requested, maxStr) >= 0) return false;
            }
        }

        return true;
    }

    private static int CompareVersions(string v1, string v2)
    {
        if (Version.TryParse(v1, out var parsed1) && Version.TryParse(v2, out var parsed2))
        {
            return parsed1.CompareTo(parsed2);
        }

        if (double.TryParse(v1, NumberStyles.Any, CultureInfo.InvariantCulture, out double d1) &&
            double.TryParse(v2, NumberStyles.Any, CultureInfo.InvariantCulture, out double d2))
        {
            return d1.CompareTo(d2);
        }

        return string.Compare(v1, v2, StringComparison.OrdinalIgnoreCase);
    }
}

public sealed class FeatureEntitlementEngine
{
    private readonly IFeatureEntitlementStore _store;
    private readonly TimeProvider _timeProvider;

    public FeatureEntitlementEngine(IFeatureEntitlementStore store, TimeProvider timeProvider)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _timeProvider = timeProvider ?? throw new ArgumentNullException(nameof(timeProvider));
    }

    public async Task<IReadOnlyList<string>> ExpandFeaturesAsync(
        string tenantId,
        IReadOnlyList<string> requestedCodes,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(requestedCodes);

        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var code in requestedCodes)
        {
            if (string.IsNullOrWhiteSpace(code)) continue;

            var suite = await _store.GetPackageSuiteByCodeAsync(tenantId, code.Trim(), ct).ConfigureAwait(false);
            if (suite is not null && suite.FeatureCodes.Count > 0)
            {
                foreach (var fc in suite.FeatureCodes)
                {
                    result.Add(fc);
                }
            }
            else
            {
                result.Add(code.Trim());
            }
        }

        return result.ToList();
    }

    public async Task<FeatureAcquisitionResult> AcquireFeatureAsync(
        FeatureAcquisitionRequest request,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var now = _timeProvider.GetUtcNow();

        // 1. Resolve license entitlements
        var entitlements = await _store.GetEntitlementsForLicenseAsync(request.LicenseId, ct).ConfigureAwait(false);

        // Check if there is an explicit entitlement for this feature
        var entitlement = entitlements.FirstOrDefault(e =>
            string.Equals(e.FeatureCode, request.FeatureCode, StringComparison.OrdinalIgnoreCase));

        int? maxSeats = null;
        if (entitlement is not null)
        {
            if (!entitlement.IsEnabled)
            {
                return FeatureAcquisitionResult.Denied(request.FeatureCode, request.Version, "feature-disabled");
            }

            if (!VersionRangeMatcher.IsVersionAllowed(request.Version, entitlement.AllowedVersionRange))
            {
                return FeatureAcquisitionResult.Denied(request.FeatureCode, request.Version, "version-not-allowed");
            }

            maxSeats = entitlement.MaxSeats;
        }

        // 2. Also check if the feature definition exists in the catalog
        var def = await _store.GetFeatureDefinitionByCodeAsync(request.TenantId, request.FeatureCode, ct).ConfigureAwait(false);
        if (def is not null)
        {
            if (!def.IsFloating)
            {
                // Non-floating feature is automatically granted without seat tracking
                return FeatureAcquisitionResult.Ok(request.FeatureCode, request.Version, inUse: 1, maxSeats: null);
            }

            if (maxSeats is null && def.DefaultMaxSeats.HasValue)
            {
                maxSeats = def.DefaultMaxSeats;
            }
        }

        // 3. Atomically acquire
        var lease = await _store.TryAcquireFeatureLeaseAsync(request, maxSeats, now, ct).ConfigureAwait(false);
        if (lease is null)
        {
            int currentInUse = await _store.GetActiveFeatureCountAsync(request.LicenseId, request.FeatureCode, now, ct).ConfigureAwait(false);
            return FeatureAcquisitionResult.Denied(request.FeatureCode, request.Version, "feature-capacity-exceeded", inUse: currentInUse, maxSeats: maxSeats);
        }

        int newInUse = await _store.GetActiveFeatureCountAsync(request.LicenseId, request.FeatureCode, now, ct).ConfigureAwait(false);
        return FeatureAcquisitionResult.Ok(request.FeatureCode, request.Version, inUse: newInUse, maxSeats: maxSeats);
    }

    public async Task<FeatureReleaseResult> ReleaseFeatureAsync(
        string leaseId,
        string featureCode,
        CancellationToken ct = default)
    {
        bool released = await _store.ReleaseFeatureLeaseAsync(leaseId, featureCode, ct).ConfigureAwait(false);
        return released ? FeatureReleaseResult.Ok(featureCode) : FeatureReleaseResult.NotFound(featureCode);
    }

    public async Task<int> ReleaseAllFeaturesForLeaseAsync(string leaseId, CancellationToken ct = default)
    {
        return await _store.ReleaseAllFeaturesForLeaseAsync(leaseId, ct).ConfigureAwait(false);
    }
}
