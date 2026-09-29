using System;
using System.Collections.Generic;
using Symbolon.Domain.Reporting;
using Xunit;

namespace Symbolon.Domain.Tests;

public sealed class AuditReportingTests
{
    [Fact]
    public void ConcurrencyCalculator_Computes_Exact_Step_Function_Peak_And_Averages()
    {
        var baseTime = new DateTimeOffset(2026, 9, 28, 10, 0, 0, TimeSpan.Zero);
        var events = new List<ConcurrencyAuditEvent>
        {
            new("ev1", baseTime.AddMinutes(5), "checkout", "lic-01", "dev-1", Quantity: 1),
            new("ev2", baseTime.AddMinutes(15), "checkout", "lic-01", "dev-2", Quantity: 1),
            new("ev3", baseTime.AddMinutes(30), "checkout", "lic-01", "dev-3", Quantity: 1),
            new("ev4", baseTime.AddMinutes(45), "release", "lic-01", "dev-1", Quantity: 1),
            new("ev5", baseTime.AddMinutes(50), "deny", "lic-01", "dev-4", Quantity: 1)
        };

        var report = AuditPeakConcurrencyCalculator.Calculate(
            events,
            baseTime,
            baseTime.AddHours(2),
            "hour",
            "lic-01",
            licensedCapacity: 5,
            initialActiveSeats: 0
        );

        Assert.Equal(3, report.OverallPeak);
        Assert.Equal(60.0, report.OverallPeakUtilizationPercent);
        Assert.Equal(3, report.TotalCheckouts);
        Assert.Equal(1, report.TotalDenials);
        Assert.Equal(2, report.Buckets.Count);

        // Hour 1: peak was 3, 3 checkouts, 1 denial
        var h1 = report.Buckets[0];
        Assert.Equal(3, h1.PeakConcurrency);
        Assert.Equal(3, h1.TotalCheckouts);
        Assert.Equal(1, h1.TotalDenials);
        Assert.True(h1.AverageConcurrency > 0 && h1.AverageConcurrency < 3);

        // Hour 2: starts with 2 active seats (from hour 1 release), no events -> peak 2, 0 checkouts
        var h2 = report.Buckets[1];
        Assert.Equal(2, h2.PeakConcurrency);
        Assert.Equal(2.0, h2.AverageConcurrency);
        Assert.Equal(0, h2.TotalCheckouts);
    }

    [Fact]
    public void TrueUpGenerator_Evaluates_Contract_Compliance_And_Exports_Csv()
    {
        var baseTime = new DateTimeOffset(2026, 9, 28, 0, 0, 0, TimeSpan.Zero);
        var endTime = baseTime.AddDays(7);

        var events = new List<ConcurrencyAuditEvent>
        {
            new("ev1", baseTime.AddDays(1), "checkout", "lic-01", "dev-1", Quantity: 5),
            new("ev2", baseTime.AddDays(2), "checkout", "lic-01", "dev-2", Quantity: 7), // Peak = 12
            new("ev3", baseTime.AddDays(3), "release", "lic-01", "dev-1", Quantity: 5)
        };

        // Scenario A: Compliant (Capacity 15)
        var compliantReport = TrueUpReportGenerator.Generate(
            "tenant-alpha",
            "lic-01",
            "Enterprise CAD Suite",
            licensedSeats: 15,
            overageBufferSeats: 3,
            events,
            baseTime,
            endTime,
            endTime
        );
        Assert.Equal("Compliant", compliantReport.ContractCompliance);
        Assert.Equal(12, compliantReport.PeakConcurrentSeats);
        Assert.Equal(0, compliantReport.OverageSeatsUsed);

        // Scenario B: OverageWarning (Capacity 10, Buffer 3, Peak 12 => +2 seats overage within buffer)
        var warningReport = TrueUpReportGenerator.Generate(
            "tenant-alpha",
            "lic-01",
            "Enterprise CAD Suite",
            licensedSeats: 10,
            overageBufferSeats: 3,
            events,
            baseTime,
            endTime,
            endTime
        );
        Assert.Equal("OverageWarning", warningReport.ContractCompliance);
        Assert.Equal(2, warningReport.OverageSeatsUsed);
        Assert.True(warningReport.BillableOverageSeatSeconds > 0);

        // Scenario C: NonCompliant (Capacity 10, Buffer 1, Peak 12 => +2 seats exceeds buffer 1)
        var nonCompliantReport = TrueUpReportGenerator.Generate(
            "tenant-alpha",
            "lic-01",
            "Enterprise CAD Suite",
            licensedSeats: 10,
            overageBufferSeats: 1,
            events,
            baseTime,
            endTime,
            endTime
        );
        Assert.Equal("NonCompliant", nonCompliantReport.ContractCompliance);

        // Verify CSV export format
        string csv = TrueUpReportGenerator.ExportToCsv(warningReport);
        Assert.Contains("TenantId,LicenseId,ProductName", csv, StringComparison.Ordinal);
        Assert.Contains("\"tenant-alpha\",\"lic-01\",\"Enterprise CAD Suite\",10,12,3,2,\"OverageWarning\"", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void AuditChainVerifier_Validates_Intact_Chain_And_Detects_Tampering()
    {
        var now = DateTimeOffset.UtcNow;
        var events = new List<AuditRecordItem>();
        byte[]? prevHash = null;

        for (int i = 0; i < 5; i++)
        {
            string id = $"aud_{i:D4}";
            string type = i % 2 == 0 ? "checkout" : "renew";
            string payload = $"{{\"seq\":{i}}}";
            var ts = now.AddMinutes(i * 5);
            byte[] hash = AuditChainIntegrityVerifier.ComputeHash(prevHash, id, type, "lic-01", "dev-fp", payload, ts);

            events.Add(new AuditRecordItem(id, ts, type, "lic-01", "dev-fp", payload, prevHash, hash));
            prevHash = hash;
        }

        // 1. Verify intact chain
        var intactResult = AuditChainIntegrityVerifier.Verify(events, now);
        Assert.True(intactResult.IsChainIntact);
        Assert.Equal(5, intactResult.TotalEventsVerified);
        Assert.NotEmpty(intactResult.RootHashHex);
        Assert.Null(intactResult.TamperedEventId);

        // 2. Tamper with payload of event 2
        var tamperedList = new List<AuditRecordItem>(events);
        var original = tamperedList[2];
        tamperedList[2] = original with { PayloadJson = "{\"seq\":999,\"hacked\":true}" };

        var tamperedResult = AuditChainIntegrityVerifier.Verify(tamperedList, now);
        Assert.False(tamperedResult.IsChainIntact);
        Assert.Equal(original.Id, tamperedResult.TamperedEventId);
        Assert.Contains("Cryptographic digest mismatch", tamperedResult.TamperReason, StringComparison.Ordinal);

        // 3. Broken link (tamper with prevHash of event 3)
        var brokenLinkList = new List<AuditRecordItem>(events);
        var targetEvent = brokenLinkList[3];
        brokenLinkList[3] = targetEvent with { PrevHash = new byte[32] };

        var brokenResult = AuditChainIntegrityVerifier.Verify(brokenLinkList, now);
        Assert.False(brokenResult.IsChainIntact);
        Assert.Equal(targetEvent.Id, brokenResult.TamperedEventId);
        Assert.Contains("Chain linkage broken", brokenResult.TamperReason, StringComparison.Ordinal);
    }
}
