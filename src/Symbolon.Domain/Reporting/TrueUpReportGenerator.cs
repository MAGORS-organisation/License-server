using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Symbolon.Protocol.Reporting;

namespace Symbolon.Domain.Reporting;

public static class TrueUpReportGenerator
{
    public static TrueUpReportDto Generate(
        string tenantId,
        string? licenseId,
        string productName,
        int licensedSeats,
        int overageBufferSeats,
        IReadOnlyList<ConcurrencyAuditEvent> events,
        DateTimeOffset periodStart,
        DateTimeOffset periodEnd,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(events);

        var sortedEvents = events
            .OrderBy(e => e.Timestamp)
            .ThenBy(e => e.Id)
            .ToList();

        int currentSeats = 0;
        int peakSeats = 0;
        int totalCheckouts = 0;
        int totalDenials = 0;
        long billableOverageSeconds = 0;

        DateTimeOffset lastTimestamp = periodStart;

        foreach (var ev in sortedEvents)
        {
            if (ev.Timestamp > periodEnd)
            {
                break;
            }

            if (ev.Timestamp >= periodStart)
            {
                long elapsedSeconds = (long)(ev.Timestamp - lastTimestamp).TotalSeconds;
                if (elapsedSeconds > 0 && currentSeats > licensedSeats)
                {
                    int overage = currentSeats - licensedSeats;
                    billableOverageSeconds += overage * elapsedSeconds;
                }
                lastTimestamp = ev.Timestamp;
            }

            if (string.Equals(ev.Type, "checkout", StringComparison.OrdinalIgnoreCase))
            {
                currentSeats += Math.Max(1, ev.Quantity);
                if (ev.Timestamp >= periodStart)
                {
                    totalCheckouts++;
                }
            }
            else if (string.Equals(ev.Type, "release", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(ev.Type, "expire", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(ev.Type, "return", StringComparison.OrdinalIgnoreCase))
            {
                currentSeats = Math.Max(0, currentSeats - Math.Max(1, ev.Quantity));
            }
            else if (string.Equals(ev.Type, "deny", StringComparison.OrdinalIgnoreCase))
            {
                if (ev.Timestamp >= periodStart)
                {
                    totalDenials++;
                }
            }

            if (ev.Timestamp >= periodStart && currentSeats > peakSeats)
            {
                peakSeats = currentSeats;
            }
        }

        // Remainder to period end
        if (periodEnd > lastTimestamp && currentSeats > licensedSeats)
        {
            long remainingSeconds = (long)(periodEnd - lastTimestamp).TotalSeconds;
            if (remainingSeconds > 0)
            {
                int overage = currentSeats - licensedSeats;
                billableOverageSeconds += overage * remainingSeconds;
            }
        }

        int overageSeatsUsed = Math.Max(0, peakSeats - licensedSeats);

        string contractCompliance = overageSeatsUsed switch
        {
            0 => "Compliant",
            _ when overageSeatsUsed <= overageBufferSeats => "OverageWarning",
            _ => "NonCompliant"
        };

        return new TrueUpReportDto(
            TenantId: tenantId,
            LicenseId: licenseId,
            ProductName: productName,
            LicensedSeats: licensedSeats,
            PeakConcurrentSeats: peakSeats,
            OverageBufferSeats: overageBufferSeats,
            OverageSeatsUsed: overageSeatsUsed,
            ContractCompliance: contractCompliance,
            TotalCheckouts: totalCheckouts,
            TotalDenials: totalDenials,
            BillableOverageSeatSeconds: billableOverageSeconds,
            PeriodStart: periodStart,
            PeriodEnd: periodEnd,
            GeneratedAtUtc: now
        );
    }

    public static string ExportToCsv(TrueUpReportDto report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var sb = new StringBuilder();
        sb.AppendLine("TenantId,LicenseId,ProductName,LicensedSeats,PeakConcurrentSeats,OverageBufferSeats,OverageSeatsUsed,ContractCompliance,TotalCheckouts,TotalDenials,BillableOverageSeatSeconds,PeriodStart,PeriodEnd,GeneratedAtUtc");
        sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
            "\"{0}\",\"{1}\",\"{2}\",{3},{4},{5},{6},\"{7}\",{8},{9},{10},\"{11:O}\",\"{12:O}\",\"{13:O}\"",
            EscapeCsv(report.TenantId),
            EscapeCsv(report.LicenseId ?? "All"),
            EscapeCsv(report.ProductName),
            report.LicensedSeats,
            report.PeakConcurrentSeats,
            report.OverageBufferSeats,
            report.OverageSeatsUsed,
            report.ContractCompliance,
            report.TotalCheckouts,
            report.TotalDenials,
            report.BillableOverageSeatSeconds,
            report.PeriodStart,
            report.PeriodEnd,
            report.GeneratedAtUtc
        ));

        return sb.ToString();
    }

    private static string EscapeCsv(string s) => s.Replace("\"", "\"\"", StringComparison.Ordinal);
}
