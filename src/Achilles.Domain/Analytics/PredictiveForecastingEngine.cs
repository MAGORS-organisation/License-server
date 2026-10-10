namespace Achilles.Domain.Analytics;

public sealed record HistoricalConcurrencySample(
    DateTimeOffset Timestamp,
    int ActiveSeats
);

public sealed record ConcurrencyForecastPoint(
    DateTimeOffset Timestamp,
    double PredictedSeats,
    double LowerBound,
    double UpperBound
);

public sealed record ConcurrencyForecastResult(
    int TotalCapacity,
    int PeakObserved,
    double CurrentUsage,
    double PredictedPeak,
    TimeSpan? TimeToExhaustion,
    int RecommendedCapacityBuffer,
    double WeeklyGrowthRatePercent,
    IReadOnlyList<ConcurrencyForecastPoint> ForecastPoints
);

public static class PredictiveForecastingEngine
{
    /// <summary>
    /// Computes a predictive concurrency forecast using Double Exponential Smoothing (Holt's Linear Trend)
    /// and linear regression with confidence intervals.
    /// </summary>
    public static ConcurrencyForecastResult Forecast(
        IReadOnlyList<HistoricalConcurrencySample> samples,
        int totalCapacity,
        int forecastHours = 24,
        double alpha = 0.3,
        double beta = 0.1)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (totalCapacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(totalCapacity), "Total capacity must be positive.");
        }

        if (samples.Count == 0)
        {
            var now = DateTimeOffset.UtcNow;
            return new ConcurrencyForecastResult(
                TotalCapacity: totalCapacity,
                PeakObserved: 0,
                CurrentUsage: 0,
                PredictedPeak: 0,
                TimeToExhaustion: null,
                RecommendedCapacityBuffer: Math.Max(1, (int)(totalCapacity * 0.2)),
                WeeklyGrowthRatePercent: 0,
                ForecastPoints: Array.Empty<ConcurrencyForecastPoint>()
            );
        }

        var sorted = samples.OrderBy(s => s.Timestamp).ToList();
        int peakObserved = sorted.Max(s => s.ActiveSeats);
        double currentUsage = sorted[^1].ActiveSeats;

        if (sorted.Count == 1)
        {
            var points = new List<ConcurrencyForecastPoint>();
            var t = sorted[0].Timestamp;
            for (int i = 1; i <= forecastHours; i++)
            {
                points.Add(new ConcurrencyForecastPoint(
                    t.AddHours(i),
                    currentUsage,
                    Math.Max(0, currentUsage - 1),
                    Math.Min(totalCapacity, currentUsage + 1)
                ));
            }

            return new ConcurrencyForecastResult(
                TotalCapacity: totalCapacity,
                PeakObserved: peakObserved,
                CurrentUsage: currentUsage,
                PredictedPeak: currentUsage,
                TimeToExhaustion: null,
                RecommendedCapacityBuffer: Math.Max(1, (int)(totalCapacity * 0.15)),
                WeeklyGrowthRatePercent: 0,
                ForecastPoints: points
            );
        }

        // Initialize Holt's Linear Trend
        double level = sorted[0].ActiveSeats;
        double trend = sorted[1].ActiveSeats - sorted[0].ActiveSeats;

        for (int i = 1; i < sorted.Count; i++)
        {
            double val = sorted[i].ActiveSeats;
            double prevLevel = level;
            level = alpha * val + (1 - alpha) * (level + trend);
            trend = beta * (level - prevLevel) + (1 - beta) * trend;
        }

        // Compute residuals standard deviation for confidence bounds
        double variance = 0.0;
        double simLevel = sorted[0].ActiveSeats;
        double simTrend = sorted[1].ActiveSeats - sorted[0].ActiveSeats;
        for (int i = 1; i < sorted.Count; i++)
        {
            double predicted = simLevel + simTrend;
            double diff = sorted[i].ActiveSeats - predicted;
            variance += diff * diff;

            double nextLevel = alpha * sorted[i].ActiveSeats + (1 - alpha) * (simLevel + simTrend);
            simTrend = beta * (nextLevel - simLevel) + (1 - beta) * simTrend;
            simLevel = nextLevel;
        }
        double stdDev = Math.Sqrt(variance / sorted.Count);
        if (stdDev < 0.5) stdDev = 0.5;

        // Generate Forecast Points
        var lastTime = sorted[^1].Timestamp;
        var forecastPoints = new List<ConcurrencyForecastPoint>(forecastHours);
        double maxPredicted = currentUsage;

        for (int h = 1; h <= forecastHours; h++)
        {
            double forecastValue = Math.Max(0, level + h * trend);
            double errorMargin = 1.96 * stdDev * Math.Sqrt(h); // 95% confidence interval

            double lower = Math.Max(0, forecastValue - errorMargin);
            double upper = Math.Min(totalCapacity * 1.5, forecastValue + errorMargin);

            if (forecastValue > maxPredicted)
            {
                maxPredicted = forecastValue;
            }

            forecastPoints.Add(new ConcurrencyForecastPoint(
                Timestamp: lastTime.AddHours(h),
                PredictedSeats: Math.Round(forecastValue, 2),
                LowerBound: Math.Round(lower, 2),
                UpperBound: Math.Round(upper, 2)
            ));
        }

        // Time to Exhaustion (when predicted >= totalCapacity)
        TimeSpan? timeToExhaustion = null;
        if (trend > 0.001)
        {
            double hoursToExhaustion = (totalCapacity - level) / trend;
            if (hoursToExhaustion > 0 && hoursToExhaustion <= 720) // up to 30 days
            {
                timeToExhaustion = TimeSpan.FromHours(hoursToExhaustion);
            }
        }

        // Growth rate per week
        double weeklyGrowth = level > 0 ? (trend * 24 * 7 / level) * 100.0 : 0.0;

        // Recommended buffer (headroom needed to avoid denial of service)
        int recommendedBuffer = Math.Max(2, (int)Math.Ceiling(maxPredicted * 0.25));

        return new ConcurrencyForecastResult(
            TotalCapacity: totalCapacity,
            PeakObserved: peakObserved,
            CurrentUsage: currentUsage,
            PredictedPeak: Math.Round(maxPredicted, 1),
            TimeToExhaustion: timeToExhaustion,
            RecommendedCapacityBuffer: recommendedBuffer,
            WeeklyGrowthRatePercent: Math.Round(weeklyGrowth, 2),
            ForecastPoints: forecastPoints
        );
    }
}
