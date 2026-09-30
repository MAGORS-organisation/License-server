using System.Text.Json.Serialization;

namespace Symbolon.Domain.Pqc;

public enum PqcAlgorithmCategory
{
    Classical = 0,
    Hybrid = 1,
    PostQuantum = 2
}

public enum PqcRiskLevel
{
    None = 0,
    Low = 1,
    Medium = 2,
    High = 3,
    Critical = 4
}

public sealed record PqcKeyAuditItem
{
    public required string KeyId { get; init; }
    public required string Algorithm { get; init; }
    public required PqcAlgorithmCategory Category { get; init; }
    public required string KeyUsage { get; init; }
    public required bool IsQuantumSafe { get; init; }
    public required bool IsCnsa2Compliant { get; init; }
    public required string Recommendation { get; init; }
}

public sealed record PqcLicenseAuditItem
{
    public required string LicenseId { get; init; }
    public required string Customer { get; init; }
    public required string SignatureAlgorithm { get; init; }
    public required bool IsQuantumSafe { get; init; }
    public required PqcRiskLevel RiskLevel { get; init; }
    public DateTimeOffset? ExpiresAt { get; init; }
    public required string Recommendation { get; init; }
}

public sealed record PqcReadinessReport
{
    public DateTimeOffset ScannedAt { get; init; } = DateTimeOffset.UtcNow;
    public string ActiveProfile { get; init; } = "hybrid-v1";
    public double ReadinessScorePercent { get; init; }
    public int TotalKeysScanned { get; init; }
    public int QuantumSafeKeys { get; init; }
    public int ClassicalKeys { get; init; }
    public int TotalLicensesScanned { get; init; }
    public int QuantumSafeLicenses { get; init; }
    public int AtRiskLicenses { get; init; }
    public bool IsCnsa2Ready { get; init; }
    public bool IsNis2Ready { get; init; }
    public IReadOnlyList<PqcKeyAuditItem> KeyAudits { get; init; } = [];
    public IReadOnlyList<PqcLicenseAuditItem> LicenseAudits { get; init; } = [];
    public IReadOnlyList<string> ActionItems { get; init; } = [];
    public string Summary { get; init; } = "";
}
