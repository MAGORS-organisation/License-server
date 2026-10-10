using FluentAssertions;
using Symbolon.Domain.Analytics;
using Xunit;

namespace Symbolon.Domain.Tests;

public sealed class PredictiveForecastingTests
{
    [Fact]
    public void Forecast_EmptySamples_ReturnsDefaultZeroUsage()
    {
        var result = PredictiveForecastingEngine.Forecast(Array.Empty<HistoricalConcurrencySample>(), totalCapacity: 50);

        result.TotalCapacity.Should().Be(50);
        result.PeakObserved.Should().Be(0);
        result.CurrentUsage.Should().Be(0);
        result.ForecastPoints.Should().BeEmpty();
        result.TimeToExhaustion.Should().BeNull();
    }

    [Fact]
    public void Forecast_SingleSample_ProjectsConstantTrajectory()
    {
        var sample = new HistoricalConcurrencySample(DateTimeOffset.UtcNow, 25);
        var result = PredictiveForecastingEngine.Forecast(new[] { sample }, totalCapacity: 100, forecastHours: 12);

        result.TotalCapacity.Should().Be(100);
        result.CurrentUsage.Should().Be(25);
        result.PeakObserved.Should().Be(25);
        result.ForecastPoints.Should().HaveCount(12);
        result.ForecastPoints[0].PredictedSeats.Should().Be(25);
    }

    [Fact]
    public void Forecast_UpwardTrend_DetectsGrowthAndPredictsPeak()
    {
        var now = DateTimeOffset.UtcNow;
        var samples = new List<HistoricalConcurrencySample>();

        // 10 hours with increasing usage: 10, 12, 14, 16, 18, 20...
        for (int i = 0; i < 10; i++)
        {
            samples.Add(new HistoricalConcurrencySample(now.AddHours(-9 + i), 10 + i * 2));
        }

        var result = PredictiveForecastingEngine.Forecast(samples, totalCapacity: 40, forecastHours: 24);

        result.PeakObserved.Should().Be(28);
        result.CurrentUsage.Should().Be(28);
        result.PredictedPeak.Should().BeGreaterThan(28);
        result.WeeklyGrowthRatePercent.Should().BeGreaterThan(0);
        result.RecommendedCapacityBuffer.Should().BeGreaterThan(5);

        // Should detect capacity exhaustion within 30 days
        result.TimeToExhaustion.Should().NotBeNull();
        result.TimeToExhaustion!.Value.TotalHours.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Forecast_ConfidenceIntervals_ExpandOverTime()
    {
        var now = DateTimeOffset.UtcNow;
        var samples = new List<HistoricalConcurrencySample>
        {
            new(now.AddHours(-3), 20),
            new(now.AddHours(-2), 22),
            new(now.AddHours(-1), 21),
            new(now, 23)
        };

        var result = PredictiveForecastingEngine.Forecast(samples, totalCapacity: 50, forecastHours: 6);

        result.ForecastPoints.Should().HaveCount(6);
        // Error margin at hour 6 should be wider than hour 1
        var diff1 = result.ForecastPoints[0].UpperBound - result.ForecastPoints[0].LowerBound;
        var diff6 = result.ForecastPoints[5].UpperBound - result.ForecastPoints[5].LowerBound;
        diff6.Should().BeGreaterThan(diff1);
    }
}
