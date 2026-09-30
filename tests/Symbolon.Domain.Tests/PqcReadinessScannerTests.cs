using FluentAssertions;
using Symbolon.Crypto;
using Symbolon.Domain.Pqc;
using Xunit;

namespace Symbolon.Domain.Tests;

public class PqcReadinessScannerTests
{
    [Fact]
    public void Scan_WithPureClassicalArtifacts_ReportsLowReadinessAndHighHndlRisk()
    {
        var keys = new List<JsonWebKeyDto>
        {
            new() { Kty = "EC", Alg = Alg.Es256, Kid = "classical-ec-key" }
        };

        var licenses = new List<(string Id, string Customer, string? Alg, DateTimeOffset? ExpiresAt)>
        {
            ("lic_perpetual_1", "Acme Defense", Alg.Es256, DateTimeOffset.UtcNow.AddYears(15)), // High risk: expires after 2030
            ("lic_short_term", "Small Business", Alg.Es256, DateTimeOffset.UtcNow.AddMonths(3))    // Low risk: expires before 2030
        };

        var report = PqcReadinessScanner.Scan(keys, licenses, activeProfile: PqcProfiles.HybridV1);

        report.ReadinessScorePercent.Should().BeLessThan(50.0);
        report.TotalKeysScanned.Should().Be(1);
        report.QuantumSafeKeys.Should().Be(0);
        report.ClassicalKeys.Should().Be(1);
        report.AtRiskLicenses.Should().Be(2);
        report.IsCnsa2Ready.Should().BeFalse();

        // High risk license check
        var highRiskLic = report.LicenseAudits.First(l => l.LicenseId == "lic_perpetual_1");
        highRiskLic.RiskLevel.Should().Be(PqcRiskLevel.High);
        highRiskLic.Recommendation.Should().Contain("Harvest-Now-Decrypt-Later");

        // Action items check
        report.ActionItems.Should().NotBeEmpty();
    }

    [Fact]
    public void Scan_WithPurePostQuantumArtifacts_Reports100PercentAndCnsa2Compliance()
    {
        var keys = new List<JsonWebKeyDto>
        {
            new() { Kty = "AKP", Alg = Alg.MlDsa65, Kid = "pqc-sig-key" },
            new() { Kty = "AKP", Alg = Alg.MlKem768, Kid = "pqc-kem-key" }
        };

        var licenses = new List<(string Id, string Customer, string? Alg, DateTimeOffset? ExpiresAt)>
        {
            ("lic_pqc_1", "Government Agency", Alg.MlDsa65, DateTimeOffset.UtcNow.AddYears(20))
        };

        var report = PqcReadinessScanner.Scan(keys, licenses, activeProfile: PqcProfiles.PqcStrict);

        report.ReadinessScorePercent.Should().Be(100.0);
        report.QuantumSafeKeys.Should().Be(2);
        report.ClassicalKeys.Should().Be(0);
        report.QuantumSafeLicenses.Should().Be(1);
        report.AtRiskLicenses.Should().Be(0);
        report.IsCnsa2Ready.Should().BeTrue();
        report.IsNis2Ready.Should().BeTrue();
        report.Summary.Should().Contain("100% pripravená na post-kvantovú éru");
    }
}
