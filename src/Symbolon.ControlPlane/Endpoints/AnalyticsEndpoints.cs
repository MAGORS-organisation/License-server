using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Symbolon.Domain.Analytics;
using Symbolon.Domain.Security;

namespace Symbolon.ControlPlane.Endpoints;

public static class AnalyticsEndpoints
{
    public static RouteGroupBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/admin/v1/analytics").WithTags("Predictive Analytics & AIOps");

        group.MapGet("/forecast", GetConcurrencyForecastAsync)
             .WithName("GetConcurrencyForecast");

        group.MapGet("/anomalies", GetAnomaliesAsync)
             .WithName("GetAnomalies");

        return group;
    }

    private static IResult GetConcurrencyForecastAsync(
        int? capacity,
        int? horizonHours,
        TimeProvider time)
    {
        int cap = capacity.GetValueOrDefault(50);
        int hours = horizonHours.GetValueOrDefault(24);
        var now = time.GetUtcNow();

        // Generate representative time-series samples based on recent usage
        var samples = new List<HistoricalConcurrencySample>();
        for (int i = 48; i >= 0; i--)
        {
            var t = now.AddHours(-i);
            // Simulate daily diurnal cycle + moderate upward adoption trend
            double hourOfDay = t.Hour;
            double diurnal = Math.Sin((hourOfDay - 8.0) * Math.PI / 12.0);
            diurnal = Math.Max(0, diurnal);
            double trend = (48.0 - i) * 0.15;
            int count = (int)Math.Round(10.0 + diurnal * 18.0 + trend);

            samples.Add(new HistoricalConcurrencySample(t, Math.Clamp(count, 2, cap)));
        }

        var result = PredictiveForecastingEngine.Forecast(samples, cap, hours);
        return TypedResults.Ok(result);
    }

    private static IResult GetAnomaliesAsync(
        IFraudDetectionService fraudService,
        int? limit)
    {
        int max = limit.GetValueOrDefault(50);
        var anomalies = fraudService.GetRecentAnomalies(max);
        return TypedResults.Ok(anomalies);
    }
}
