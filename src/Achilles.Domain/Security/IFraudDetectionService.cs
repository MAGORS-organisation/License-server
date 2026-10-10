namespace Achilles.Domain.Security;

public sealed record GeoLocation(double Latitude, double Longitude, string City, string Country);

public sealed record FraudAccessEvent(
    string LicenseId,
    string FingerprintHash,
    IReadOnlyDictionary<string, string>? FingerprintComponents,
    string? MachineId,
    string? UserId,
    string? IpAddress,
    GeoLocation? Location,
    DateTimeOffset Timestamp);

public enum FraudRiskLevel
{
    None,
    Low,
    Medium,
    High,
    Critical
}

public sealed record FraudAssessmentResult(
    bool IsSuspicious,
    FraudRiskLevel RiskLevel,
    string? RiskType,
    string? Description,
    double? VelocityKmH,
    double? DistanceKm,
    GeoLocation? PreviousLocation,
    GeoLocation? CurrentLocation,
    DateTimeOffset? PreviousTimestamp);

public sealed record FraudRecord(
    string Id,
    string LicenseId,
    string? UserId,
    string? MachineId,
    string? IpAddress,
    string RiskType,
    FraudRiskLevel RiskLevel,
    string Description,
    double? VelocityKmH,
    double? DistanceKm,
    DateTimeOffset Timestamp);

public interface IFraudDetectionService
{
    Task<FraudAssessmentResult> EvaluateAccessAsync(
        FraudAccessEvent accessEvent,
        CancellationToken ct = default);

    IReadOnlyList<FraudRecord> GetRecentAnomalies(int limit = 50);

    void Clear();
}
