using System.Diagnostics.Metrics;
using System.Text;

namespace Symbolon.ControlPlane.Observability;

public sealed class SymbolonMetrics : IDisposable
{
    public const string MeterName = "Symbolon.ControlPlane";

    private readonly Meter _meter;
    private readonly UpDownCounter<long> _activeSeatsCounter;
    private readonly Counter<long> _checkoutDeniedCounter;
    private readonly Histogram<double> _checkoutDurationHistogram;
    private readonly Counter<long> _leasesRenewedCounter;
    private readonly Counter<long> _leasesReleasedCounter;
    private readonly Counter<long> _skewDetectedCounter;

    private long _activeSeats;
    private long _totalDenied;
    private long _totalRenewed;
    private long _totalReleased;
    private long _totalSkew;

    public SymbolonMetrics()
    {
        _meter = new Meter(MeterName, "1.0.0");

        _activeSeatsCounter = _meter.CreateUpDownCounter<long>(
            "symbolon.seats.active",
            description: "Current number of active seat allocations.");

        _checkoutDeniedCounter = _meter.CreateCounter<long>(
            "symbolon.checkout.denied",
            description: "Total number of denied checkout requests (upsell signal).");

        _checkoutDurationHistogram = _meter.CreateHistogram<double>(
            "symbolon.checkout.duration",
            unit: "ms",
            description: "Latency of checkout operations in milliseconds.");

        _leasesRenewedCounter = _meter.CreateCounter<long>(
            "symbolon.lease.renewed",
            description: "Total number of successful lease renewals.");

        _leasesReleasedCounter = _meter.CreateCounter<long>(
            "symbolon.lease.released",
            description: "Total number of explicit lease releases.");

        _skewDetectedCounter = _meter.CreateCounter<long>(
            "symbolon.clock.skew_detected",
            description: "Total number of clock skew anomalies detected.");
    }

    public void RecordSeatAcquired(int quantity = 1)
    {
        Interlocked.Add(ref _activeSeats, quantity);
        _activeSeatsCounter.Add(quantity);
    }

    public void RecordSeatReleased(int quantity = 1)
    {
        Interlocked.Add(ref _activeSeats, -quantity);
        Interlocked.Increment(ref _totalReleased);
        _activeSeatsCounter.Add(-quantity);
        _leasesReleasedCounter.Add(quantity);
    }

    public void RecordCheckoutDenied(string licenseId)
    {
        Interlocked.Increment(ref _totalDenied);
        _checkoutDeniedCounter.Add(1, new KeyValuePair<string, object?>("license_id", licenseId));
    }

    public void RecordCheckoutDuration(double milliseconds)
    {
        _checkoutDurationHistogram.Record(milliseconds);
    }

    public void RecordLeaseRenewed()
    {
        Interlocked.Increment(ref _totalRenewed);
        _leasesRenewedCounter.Add(1);
    }

    public void RecordClockSkew()
    {
        Interlocked.Increment(ref _totalSkew);
        _skewDetectedCounter.Add(1);
    }

    public string GeneratePrometheusMetrics()
    {
        var sb = new StringBuilder(512);

        sb.AppendLine("# HELP symbolon_seats_active Current number of active seat allocations.");
        sb.AppendLine("# TYPE symbolon_seats_active gauge");
        sb.Append("symbolon_seats_active ").AppendLine(Volatile.Read(ref _activeSeats).ToString(System.Globalization.CultureInfo.InvariantCulture));

        sb.AppendLine("# HELP symbolon_checkout_denied_total Total number of denied checkout requests.");
        sb.AppendLine("# TYPE symbolon_checkout_denied_total counter");
        sb.Append("symbolon_checkout_denied_total ").AppendLine(Volatile.Read(ref _totalDenied).ToString(System.Globalization.CultureInfo.InvariantCulture));

        sb.AppendLine("# HELP symbolon_lease_renewed_total Total number of successful lease renewals.");
        sb.AppendLine("# TYPE symbolon_lease_renewed_total counter");
        sb.Append("symbolon_lease_renewed_total ").AppendLine(Volatile.Read(ref _totalRenewed).ToString(System.Globalization.CultureInfo.InvariantCulture));

        sb.AppendLine("# HELP symbolon_lease_released_total Total number of explicit lease releases.");
        sb.AppendLine("# TYPE symbolon_lease_released_total counter");
        sb.Append("symbolon_lease_released_total ").AppendLine(Volatile.Read(ref _totalReleased).ToString(System.Globalization.CultureInfo.InvariantCulture));

        sb.AppendLine("# HELP symbolon_clock_skew_detected_total Total number of clock skew anomalies detected.");
        sb.AppendLine("# TYPE symbolon_clock_skew_detected_total counter");
        sb.Append("symbolon_clock_skew_detected_total ").AppendLine(Volatile.Read(ref _totalSkew).ToString(System.Globalization.CultureInfo.InvariantCulture));

        return sb.ToString();
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}
