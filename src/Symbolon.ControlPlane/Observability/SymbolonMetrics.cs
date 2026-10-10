using System.Diagnostics.Metrics;
using System.Text;

namespace Symbolon.ControlPlane.Observability;

public sealed class SymbolonMetrics : IDisposable
{
    public const string MeterName = "Symbolon.ControlPlane";

    private readonly Meter _meter;
    private readonly UpDownCounter<long> _activeSeatsCounter;
    private readonly Counter<long> _checkoutDeniedCounter;
    private readonly Counter<long> _checkoutSuccessCounter;
    private readonly Counter<long> _pqcSignaturesCounter;
    private readonly Counter<long> _tokensConsumedCounter;
    private readonly Histogram<double> _checkoutDurationHistogram;
    private readonly Counter<long> _leasesRenewedCounter;
    private readonly Counter<long> _leasesReleasedCounter;
    private readonly Counter<long> _skewDetectedCounter;
    private readonly Counter<long> _alertsTriggeredCounter;

    private long _activeSeats;
    private long _totalDenied;
    private long _totalSuccess;
    private long _totalPqcSignatures;
    private long _totalClassicalSignatures;
    private long _totalTokensConsumed;
    private long _totalRenewed;
    private long _totalReleased;
    private long _totalSkew;
    private long _totalAlerts;
    private long _totalCapacity = 1000;
    private long _totalCryptoKeys = 1;
    private readonly DateTimeOffset _startedAt = DateTimeOffset.UtcNow;

    public SymbolonMetrics()
    {
        _meter = new Meter(MeterName, "1.0.0");

        _activeSeatsCounter = _meter.CreateUpDownCounter<long>(
            "symbolon.seats.active",
            description: "Current number of active seat allocations.");

        _checkoutDeniedCounter = _meter.CreateCounter<long>(
            "symbolon.checkout.denied",
            description: "Total number of denied checkout requests (upsell signal).");

        _checkoutSuccessCounter = _meter.CreateCounter<long>(
            "symbolon.checkout.success",
            description: "Total number of successful checkout operations.");

        _pqcSignaturesCounter = _meter.CreateCounter<long>(
            "symbolon.pqc.signatures",
            description: "Total cryptographic signatures issued by algorithm.");

        _tokensConsumedCounter = _meter.CreateCounter<long>(
            "symbolon.tokens.consumed",
            description: "Total tokens or metered credits consumed.");

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

        _alertsTriggeredCounter = _meter.CreateCounter<long>(
            "symbolon.alerts.triggered",
            description: "Total number of enterprise security and capacity alerts triggered.");
    }

    public long ActiveSeats => Volatile.Read(ref _activeSeats);

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

    public void RecordCheckoutSuccess(string algorithm = "es256")
    {
        Interlocked.Increment(ref _totalSuccess);
        _checkoutSuccessCounter.Add(1);
        RecordSignature(algorithm);
    }

    public void RecordSignature(string algorithm)
    {
        if (algorithm.Contains("mldsa", StringComparison.OrdinalIgnoreCase) || algorithm.Contains("dilithium", StringComparison.OrdinalIgnoreCase))
        {
            Interlocked.Increment(ref _totalPqcSignatures);
            _pqcSignaturesCounter.Add(1, new KeyValuePair<string, object?>("algorithm", "mldsa65"));
        }
        else
        {
            Interlocked.Increment(ref _totalClassicalSignatures);
            _pqcSignaturesCounter.Add(1, new KeyValuePair<string, object?>("algorithm", "es256"));
        }
    }

    public void RecordTokensConsumed(long amount)
    {
        Interlocked.Add(ref _totalTokensConsumed, amount);
        _tokensConsumedCounter.Add(amount);
    }

    public void SetTotalCapacity(long capacity)
    {
        Volatile.Write(ref _totalCapacity, capacity);
    }

    public void SetCryptoKeysCount(long count)
    {
        Volatile.Write(ref _totalCryptoKeys, count);
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

    public void RecordAlert(string alertType)
    {
        Interlocked.Increment(ref _totalAlerts);
        _alertsTriggeredCounter.Add(1, new KeyValuePair<string, object?>("alert_type", alertType));
    }

    public string GeneratePrometheusMetrics()
    {
        var sb = new StringBuilder(1024);

        sb.AppendLine("# HELP symbolon_seats_active Current number of active seat allocations.");
        sb.AppendLine("# TYPE symbolon_seats_active gauge");
        sb.Append("symbolon_seats_active ").AppendLine(Volatile.Read(ref _activeSeats).ToString(System.Globalization.CultureInfo.InvariantCulture));

        sb.AppendLine("# HELP symbolon_seats_total Total licensed seat capacity.");
        sb.AppendLine("# TYPE symbolon_seats_total gauge");
        sb.Append("symbolon_seats_total ").AppendLine(Volatile.Read(ref _totalCapacity).ToString(System.Globalization.CultureInfo.InvariantCulture));

        sb.AppendLine("# HELP symbolon_checkout_success_total Total number of successful checkout operations.");
        sb.AppendLine("# TYPE symbolon_checkout_success_total counter");
        sb.Append("symbolon_checkout_success_total ").AppendLine(Volatile.Read(ref _totalSuccess).ToString(System.Globalization.CultureInfo.InvariantCulture));

        sb.AppendLine("# HELP symbolon_checkout_denied_total Total number of denied checkout requests.");
        sb.AppendLine("# TYPE symbolon_checkout_denied_total counter");
        sb.Append("symbolon_checkout_denied_total ").AppendLine(Volatile.Read(ref _totalDenied).ToString(System.Globalization.CultureInfo.InvariantCulture));

        sb.AppendLine("# HELP symbolon_lease_renewed_total Total number of successful lease renewals.");
        sb.AppendLine("# TYPE symbolon_lease_renewed_total counter");
        sb.Append("symbolon_lease_renewed_total ").AppendLine(Volatile.Read(ref _totalRenewed).ToString(System.Globalization.CultureInfo.InvariantCulture));

        sb.AppendLine("# HELP symbolon_lease_released_total Total number of explicit lease releases.");
        sb.AppendLine("# TYPE symbolon_lease_released_total counter");
        sb.Append("symbolon_lease_released_total ").AppendLine(Volatile.Read(ref _totalReleased).ToString(System.Globalization.CultureInfo.InvariantCulture));

        sb.AppendLine("# HELP symbolon_pqc_signatures_total Total cryptographic signatures issued by algorithm.");
        sb.AppendLine("# TYPE symbolon_pqc_signatures_total counter");
        sb.Append("symbolon_pqc_signatures_total{algorithm=\"mldsa65\"} ").AppendLine(Volatile.Read(ref _totalPqcSignatures).ToString(System.Globalization.CultureInfo.InvariantCulture));
        sb.Append("symbolon_pqc_signatures_total{algorithm=\"es256\"} ").AppendLine(Volatile.Read(ref _totalClassicalSignatures).ToString(System.Globalization.CultureInfo.InvariantCulture));

        sb.AppendLine("# HELP symbolon_tokens_consumed_total Total tokens or metered credits consumed.");
        sb.AppendLine("# TYPE symbolon_tokens_consumed_total counter");
        sb.Append("symbolon_tokens_consumed_total ").AppendLine(Volatile.Read(ref _totalTokensConsumed).ToString(System.Globalization.CultureInfo.InvariantCulture));

        sb.AppendLine("# HELP symbolon_clock_skew_detected_total Total number of clock skew anomalies detected.");
        sb.AppendLine("# TYPE symbolon_clock_skew_detected_total counter");
        sb.Append("symbolon_clock_skew_detected_total ").AppendLine(Volatile.Read(ref _totalSkew).ToString(System.Globalization.CultureInfo.InvariantCulture));

        sb.AppendLine("# HELP symbolon_alerts_triggered_total Total number of enterprise security and capacity alerts triggered.");
        sb.AppendLine("# TYPE symbolon_alerts_triggered_total counter");
        sb.Append("symbolon_alerts_triggered_total ").AppendLine(Volatile.Read(ref _totalAlerts).ToString(System.Globalization.CultureInfo.InvariantCulture));

        sb.AppendLine("# HELP symbolon_crypto_keys_total Total active KMS and signing keys in key ring.");
        sb.AppendLine("# TYPE symbolon_crypto_keys_total gauge");
        sb.Append("symbolon_crypto_keys_total ").AppendLine(Volatile.Read(ref _totalCryptoKeys).ToString(System.Globalization.CultureInfo.InvariantCulture));

        long uptime = Math.Max(1, (long)(DateTimeOffset.UtcNow - _startedAt).TotalSeconds);
        sb.AppendLine("# HELP symbolon_server_uptime_seconds Total runtime of Symbolon Control Plane in seconds.");
        sb.AppendLine("# TYPE symbolon_server_uptime_seconds gauge");
        sb.Append("symbolon_server_uptime_seconds ").AppendLine(uptime.ToString(System.Globalization.CultureInfo.InvariantCulture));

        return sb.ToString();
    }

    public void Dispose()
    {
        _meter.Dispose();
    }
}
