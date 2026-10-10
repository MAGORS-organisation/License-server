using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;
using Achilles.Protocol.Tracing;

namespace Achilles.Domain.Security;

public sealed partial class FraudDetectionService : IFraudDetectionService
{
    private const double EarthRadiusKm = 6371.0;
    public const double DefaultMaxRealisticVelocityKmH = 900.0; // Commercial jet cruising speed threshold
    public const double MinimumDistanceThresholdKm = 100.0;      // Ignore jitter within 100km radius

    private readonly double _maxVelocityKmH;
    private readonly ILogger<FraudDetectionService>? _logger;

    // Track last access event by Identity Key (UserId, or LicenseId if anonymous)
    private readonly ConcurrentDictionary<string, FraudAccessEvent> _lastUserAccess = new();

    // Track last access event by Hardware Fingerprint (FingerprintHash)
    private readonly ConcurrentDictionary<string, FraudAccessEvent> _lastHardwareAccess = new();

    // Bounded log of detected anomalies for auditing and dashboard
    private readonly ConcurrentQueue<FraudRecord> _anomalies = new();
    private const int MaxAnomalyHistory = 100;

    public FraudDetectionService(
        double maxVelocityKmH = DefaultMaxRealisticVelocityKmH,
        ILogger<FraudDetectionService>? logger = null)
    {
        _maxVelocityKmH = maxVelocityKmH > 0 ? maxVelocityKmH : DefaultMaxRealisticVelocityKmH;
        _logger = logger;
    }

    public Task<FraudAssessmentResult> EvaluateAccessAsync(
        FraudAccessEvent accessEvent,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(accessEvent);

        using var activity = AchillesTracing.ActivitySource.StartActivity(AchillesTracing.OpFraudCheck);
        activity?.SetTag(AchillesTracing.TagLicenseId, accessEvent.LicenseId);

        var currentLocation = accessEvent.Location ?? ResolveGeoLocation(accessEvent.IpAddress);
        var effectiveEvent = accessEvent.Location is null && currentLocation is not null
            ? accessEvent with { Location = currentLocation }
            : accessEvent;

        // 1. Evaluate VM Cloning / Hardware Snapshot Replay
        var vmCloneResult = CheckVmCloning(effectiveEvent);
        if (vmCloneResult is not null)
        {
            RecordAnomaly(vmCloneResult, effectiveEvent);
            _lastHardwareAccess[effectiveEvent.FingerprintHash] = effectiveEvent;
            activity?.SetTag(AchillesTracing.TagVmCloning, true);
            activity?.SetTag(AchillesTracing.TagFraudRisk, vmCloneResult.RiskLevel.ToString());
            activity?.SetStatus(ActivityStatusCode.Error, "VM Cloning Detected");
            return Task.FromResult(vmCloneResult);
        }

        // 2. Evaluate Impossible Travel Velocity
        var travelResult = CheckImpossibleTravel(effectiveEvent, currentLocation);
        if (travelResult is not null)
        {
            RecordAnomaly(travelResult, effectiveEvent);
            UpdateTracking(effectiveEvent);
            activity?.SetTag(AchillesTracing.TagVelocityKmh, travelResult.VelocityKmH);
            activity?.SetTag(AchillesTracing.TagFraudRisk, travelResult.RiskLevel.ToString());
            activity?.SetStatus(ActivityStatusCode.Error, "Impossible Travel Velocity");
            return Task.FromResult(travelResult);
        }

        // Access is legitimate / within physical bounds
        UpdateTracking(effectiveEvent);

        activity?.SetTag(AchillesTracing.TagFraudRisk, FraudRiskLevel.None.ToString());
        activity?.SetStatus(ActivityStatusCode.Ok);
        return Task.FromResult(new FraudAssessmentResult(
            IsSuspicious: false,
            RiskLevel: FraudRiskLevel.None,
            RiskType: null,
            Description: "Access within normal physical parameters.",
            VelocityKmH: null,
            DistanceKm: null,
            PreviousLocation: null,
            CurrentLocation: currentLocation,
            PreviousTimestamp: null));
    }

    public IReadOnlyList<FraudRecord> GetRecentAnomalies(int limit = 50)
    {
        return _anomalies.Reverse().Take(Math.Max(1, limit)).ToList();
    }

    public void Clear()
    {
        _lastUserAccess.Clear();
        _lastHardwareAccess.Clear();
        _anomalies.Clear();
    }

    private FraudAssessmentResult? CheckVmCloning(FraudAccessEvent accessEvent)
    {
        if (string.IsNullOrWhiteSpace(accessEvent.IpAddress))
        {
            return null;
        }

        if (IsPrivateOrLoopbackIp(accessEvent.IpAddress))
        {
            return null; // Local network development or cluster intranet
        }

        if (_lastHardwareAccess.TryGetValue(accessEvent.FingerprintHash, out var prev))
        {
            if (!string.IsNullOrWhiteSpace(prev.IpAddress) &&
                !IsPrivateOrLoopbackIp(prev.IpAddress) &&
                !string.Equals(prev.IpAddress, accessEvent.IpAddress, StringComparison.OrdinalIgnoreCase))
            {
                var timeDiff = (accessEvent.Timestamp - prev.Timestamp).Duration();
                // If identical hardware connects from divergent external IPs within 15 minutes
                if (timeDiff < TimeSpan.FromMinutes(15))
                {
                    string shortFp = accessEvent.FingerprintHash.Length > 8
                        ? accessEvent.FingerprintHash[..8]
                        : accessEvent.FingerprintHash;

                    string desc = $"VM Cloning detected: Identical hardware fingerprint [{shortFp}] active from divergent public IPs ({prev.IpAddress} vs {accessEvent.IpAddress}) within {timeDiff.TotalMinutes:F1} minutes.";

                    if (_logger is not null)
                    {
                        LogVmCloningWarning(_logger, desc);
                    }

                    return new FraudAssessmentResult(
                        IsSuspicious: true,
                        RiskLevel: FraudRiskLevel.Critical,
                        RiskType: "vm_cloning",
                        Description: desc,
                        VelocityKmH: null,
                        DistanceKm: null,
                        PreviousLocation: prev.Location,
                        CurrentLocation: accessEvent.Location,
                        PreviousTimestamp: prev.Timestamp);
                }
            }
        }

        return null;
    }

    private FraudAssessmentResult? CheckImpossibleTravel(FraudAccessEvent accessEvent, GeoLocation? currentLocation)
    {
        if (currentLocation is null)
        {
            return null;
        }

        string trackingKey = !string.IsNullOrWhiteSpace(accessEvent.UserId)
            ? $"user:{accessEvent.UserId}"
            : $"lic:{accessEvent.LicenseId}";

        if (_lastUserAccess.TryGetValue(trackingKey, out var prev) && prev.Location is not null)
        {
            double distanceKm = CalculateDistanceKm(
                prev.Location.Latitude,
                prev.Location.Longitude,
                currentLocation.Latitude,
                currentLocation.Longitude);

            if (distanceKm >= MinimumDistanceThresholdKm)
            {
                var timeDiff = (accessEvent.Timestamp - prev.Timestamp);
                double totalHours = timeDiff.TotalHours;
                if (totalHours <= 0)
                {
                    totalHours = 1.0 / 3600.0; // 1 second minimum to avoid divide-by-zero
                }

                double velocity = distanceKm / totalHours;

                if (velocity > _maxVelocityKmH)
                {
                    string desc = $"Impossible travel detected: {distanceKm:F0} km in {Math.Max(0.1, timeDiff.TotalMinutes):F1} min ({velocity:F0} km/h > {_maxVelocityKmH:F0} km/h threshold) between {prev.Location.City}, {prev.Location.Country} and {currentLocation.City}, {currentLocation.Country}.";

                    if (_logger is not null)
                    {
                        LogImpossibleTravelWarning(_logger, desc);
                    }

                    return new FraudAssessmentResult(
                        IsSuspicious: true,
                        RiskLevel: FraudRiskLevel.Critical,
                        RiskType: "impossible_travel",
                        Description: desc,
                        VelocityKmH: Math.Round(velocity, 1),
                        DistanceKm: Math.Round(distanceKm, 1),
                        PreviousLocation: prev.Location,
                        CurrentLocation: currentLocation,
                        PreviousTimestamp: prev.Timestamp);
                }
            }
        }

        return null;
    }

    private void UpdateTracking(FraudAccessEvent accessEvent)
    {
        string trackingKey = !string.IsNullOrWhiteSpace(accessEvent.UserId)
            ? $"user:{accessEvent.UserId}"
            : $"lic:{accessEvent.LicenseId}";

        _lastUserAccess[trackingKey] = accessEvent;
        _lastHardwareAccess[accessEvent.FingerprintHash] = accessEvent;
    }

    private void RecordAnomaly(FraudAssessmentResult result, FraudAccessEvent accessEvent)
    {
        var record = new FraudRecord(
            Id: $"frd_{Guid.NewGuid():N}",
            LicenseId: accessEvent.LicenseId,
            UserId: accessEvent.UserId,
            MachineId: accessEvent.MachineId,
            IpAddress: accessEvent.IpAddress,
            RiskType: result.RiskType ?? "anomaly",
            RiskLevel: result.RiskLevel,
            Description: result.Description ?? "Fraud anomaly detected",
            VelocityKmH: result.VelocityKmH,
            DistanceKm: result.DistanceKm,
            Timestamp: DateTimeOffset.UtcNow);

        _anomalies.Enqueue(record);
        while (_anomalies.Count > MaxAnomalyHistory)
        {
            _anomalies.TryDequeue(out _);
        }
    }

    public static double CalculateDistanceKm(double lat1, double lon1, double lat2, double lon2)
    {
        double dLat = ToRadians(lat2 - lat1);
        double dLon = ToRadians(lon2 - lon1);

        double a = Math.Sin(dLat / 2.0) * Math.Sin(dLat / 2.0) +
                   Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                   Math.Sin(dLon / 2.0) * Math.Sin(dLon / 2.0);

        double c = 2.0 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
        return EarthRadiusKm * c;
    }

    private static double ToRadians(double degrees) => degrees * (Math.PI / 180.0);

    public static bool IsPrivateOrLoopbackIp(string ipStr)
    {
        if (string.IsNullOrWhiteSpace(ipStr)) return true;
        if (ipStr.Equals("localhost", StringComparison.OrdinalIgnoreCase)) return true;

        if (IPAddress.TryParse(ipStr, out var ip))
        {
            if (IPAddress.IsLoopback(ip)) return true;

            byte[] bytes = ip.GetAddressBytes();
            if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
            {
                // 10.0.0.0/8
                if (bytes[0] == 10) return true;
                // 172.16.0.0/12
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
                // 192.168.0.0/16
                if (bytes[0] == 192 && bytes[1] == 168) return true;
            }
            else if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            {
                if (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal) return true;
            }
        }

        return false;
    }

    public static GeoLocation? ResolveGeoLocation(string? ip)
    {
        if (string.IsNullOrWhiteSpace(ip) || IsPrivateOrLoopbackIp(ip))
        {
            return null;
        }

        // Built-in GeoIP table for multi-region cloud and enterprise test networks
        if (ip.StartsWith("85.237.", StringComparison.Ordinal) || ip.StartsWith("195.168.", StringComparison.Ordinal) || ip.StartsWith("91.127.", StringComparison.Ordinal))
        {
            return new GeoLocation(48.1486, 17.1077, "Bratislava", "Slovakia");
        }
        if (ip.StartsWith("84.115.", StringComparison.Ordinal) || ip.StartsWith("193.170.", StringComparison.Ordinal))
        {
            return new GeoLocation(48.2082, 16.3738, "Vienna", "Austria");
        }
        if (ip.StartsWith("133.", StringComparison.Ordinal) || ip.StartsWith("202.214.", StringComparison.Ordinal) || ip.StartsWith("103.20.", StringComparison.Ordinal))
        {
            return new GeoLocation(35.6762, 139.6503, "Tokyo", "Japan");
        }
        if (ip.StartsWith("198.51.100.", StringComparison.Ordinal) || ip.StartsWith("108.30.", StringComparison.Ordinal))
        {
            return new GeoLocation(40.7128, -74.0060, "New York", "United States");
        }
        if (ip.StartsWith("185.220.", StringComparison.Ordinal) || ip.StartsWith("141.62.", StringComparison.Ordinal))
        {
            return new GeoLocation(50.1109, 8.6821, "Frankfurt", "Germany");
        }
        if (ip.StartsWith("212.58.", StringComparison.Ordinal) || ip.StartsWith("151.101.", StringComparison.Ordinal))
        {
            return new GeoLocation(51.5074, -0.1278, "London", "United Kingdom");
        }
        if (ip.StartsWith("203.0.113.", StringComparison.Ordinal) || ip.StartsWith("139.130.", StringComparison.Ordinal))
        {
            return new GeoLocation(-33.8688, 151.2093, "Sydney", "Australia");
        }
        if (ip.StartsWith("103.1.", StringComparison.Ordinal) || ip.StartsWith("165.225.", StringComparison.Ordinal))
        {
            return new GeoLocation(1.3521, 103.8198, "Singapore", "Singapore");
        }

        return null;
    }

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "Security Anomaly [vm_cloning]: {Description}")]
    private static partial void LogVmCloningWarning(ILogger logger, string description);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "Security Anomaly [impossible_travel]: {Description}")]
    private static partial void LogImpossibleTravelWarning(ILogger logger, string description);
}
