using FluentAssertions;
using Achilles.Domain.Experiments;
using Xunit;

namespace Achilles.Domain.Tests;

public sealed class ExperimentStatisticalEngineTests
{
    [Fact]
    public void NormalCdf_ShouldMatchStandardNormalDistribution()
    {
        // Landmark z values
        ExperimentStatisticalEngine.NormalCdf(0.0).Should().BeApproximately(0.5, 0.0001);
        ExperimentStatisticalEngine.NormalCdf(1.95996).Should().BeApproximately(0.975, 0.0005);
        ExperimentStatisticalEngine.NormalCdf(-1.95996).Should().BeApproximately(0.025, 0.0005);
        ExperimentStatisticalEngine.NormalCdf(2.576).Should().BeApproximately(0.995, 0.0005);
    }

    [Fact]
    public void CalculateTwoProportionZTest_WhenRatesIdentical_ShouldYieldHighPValue()
    {
        // 500 successes out of 1000 in both groups (pA = 0.5, pB = 0.5)
        var (zScore, pValue, ciLower, ciUpper) = ExperimentStatisticalEngine.CalculateTwoProportionZTest(
            500, 1000,
            500, 1000);

        zScore.Should().BeApproximately(0.0, 0.001);
        pValue.Should().BeApproximately(1.0, 0.001);
        ciLower.Should().BeLessThan(0.0);
        ciUpper.Should().BeGreaterThan(0.0);
    }

    [Fact]
    public void CalculateTwoProportionZTest_WhenSignificantDifference_ShouldYieldLowPValue()
    {
        // Control: 100/1000 = 10%
        // Treatment: 160/1000 = 16%
        var (zScore, pValue, ciLower, ciUpper) = ExperimentStatisticalEngine.CalculateTwoProportionZTest(
            100, 1000,
            160, 1000);

        zScore.Should().BeGreaterThan(3.5);
        pValue.Should().BeLessThan(0.001);
        ciLower.Should().BeGreaterThan(0.02);
        ciUpper.Should().BeLessThan(0.10);
    }

    [Fact]
    public void CalculateWelchTTest_WhenLatencyImprovesSignificantly_ShouldDetectDifference()
    {
        // Control: mean 50ms, std 10, N 500
        // Treatment: mean 35ms, std 8, N 500
        var (tScore, pValue) = ExperimentStatisticalEngine.CalculateWelchTTest(
            50.0, 10.0, 500,
            35.0, 8.0, 500);

        tScore.Should().BeLessThan(-20.0);
        pValue.Should().BeLessThan(0.0001);
    }

    [Fact]
    public void GenerateReport_WithLowSamples_RecommendsMoreData()
    {
        var exp = new Experiment
        {
            Id = "exp_small",
            Name = "Small experiment",
            Status = ExperimentStatus.Active,
            Variants =
            [
                new ExperimentVariant { VariantId = "ctrl", Name = "Ctrl", IsControl = true },
                new ExperimentVariant { VariantId = "treat", Name = "Treat", IsControl = false }
            ]
        };

        var metrics = new List<VariantMetrics>
        {
            new() { VariantId = "ctrl", TotalRequests = 10, SuccessfulCheckouts = 9 },
            new() { VariantId = "treat", TotalRequests = 12, SuccessfulCheckouts = 11 }
        };

        var report = ExperimentStatisticalEngine.GenerateReport(exp, metrics);

        report.IsStatisticallySignificant.Should().BeFalse();
        report.Recommendation.Should().Contain("Nedostatok vzoriek");
    }

    [Fact]
    public void GenerateReport_WithHighErrorRateInTreatment_RecommendsRollback()
    {
        var exp = new Experiment
        {
            Id = "exp_err",
            Name = "Error experiment",
            Status = ExperimentStatus.Active,
            Variants =
            [
                new ExperimentVariant { VariantId = "ctrl", Name = "Ctrl", IsControl = true },
                new ExperimentVariant { VariantId = "treat", Name = "Treat", IsControl = false }
            ]
        };

        var metrics = new List<VariantMetrics>
        {
            new() { VariantId = "ctrl", TotalRequests = 500, SuccessfulCheckouts = 495, Errors = 5 }, // 1% error
            new() { VariantId = "treat", TotalRequests = 500, SuccessfulCheckouts = 450, Errors = 50 } // 10% error
        };

        var report = ExperimentStatisticalEngine.GenerateReport(exp, metrics);

        report.Recommendation.Should().Contain("VAROVANIE");
        report.Recommendation.Should().Contain("rollback");
    }

    [Fact]
    public void GenerateReport_WithSignificantWinner_RecommendsPromotion()
    {
        var exp = new Experiment
        {
            Id = "exp_win",
            Name = "Winning experiment",
            Status = ExperimentStatus.Active,
            Variants =
            [
                new ExperimentVariant { VariantId = "ctrl", Name = "Ctrl", IsControl = true },
                new ExperimentVariant { VariantId = "treat", Name = "Treat", IsControl = false }
            ]
        };

        var metrics = new List<VariantMetrics>
        {
            new() { VariantId = "ctrl", TotalRequests = 1000, SuccessfulCheckouts = 800 }, // 80%
            new() { VariantId = "treat", TotalRequests = 1000, SuccessfulCheckouts = 900 } // 90%
        };

        var report = ExperimentStatisticalEngine.GenerateReport(exp, metrics);

        report.IsStatisticallySignificant.Should().BeTrue();
        report.PValue.Should().BeLessThan(0.001);
        report.Recommendation.Should().Contain("ODPORÚČANIE");
        report.Recommendation.Should().Contain("100%");
    }
}
