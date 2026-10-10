using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Symbolon.Protocol.Reporting;

/// <summary>
/// Aggregated concurrency bucket for a discrete time window (hour, day, week, month).
/// Conforms to spec/07-floating-protokol.md FLT-38 (calculated from audit events).
/// </summary>
public sealed record ConcurrencyTimeBucketDto(
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp,
    [property: JsonPropertyName("peakConcurrency")] int PeakConcurrency,
    [property: JsonPropertyName("averageConcurrency")] double AverageConcurrency,
    [property: JsonPropertyName("totalCheckouts")] int TotalCheckouts,
    [property: JsonPropertyName("totalDenials")] int TotalDenials,
    [property: JsonPropertyName("capacityLimit")] int CapacityLimit,
    [property: JsonPropertyName("peakUtilizationPercent")] double PeakUtilizationPercent
);

/// <summary>
/// Complete historical concurrency analytics report over a given time range.
/// </summary>
public sealed record ConcurrencyAnalyticsResponseDto(
    [property: JsonPropertyName("rangeStart")] DateTimeOffset RangeStart,
    [property: JsonPropertyName("rangeEnd")] DateTimeOffset RangeEnd,
    [property: JsonPropertyName("bucketSize")] string BucketSize,
    [property: JsonPropertyName("licenseId")] string? LicenseId,
    [property: JsonPropertyName("licensedCapacity")] int LicensedCapacity,
    [property: JsonPropertyName("overallPeak")] int OverallPeak,
    [property: JsonPropertyName("overallPeakUtilizationPercent")] double OverallPeakUtilizationPercent,
    [property: JsonPropertyName("totalCheckouts")] int TotalCheckouts,
    [property: JsonPropertyName("totalDenials")] int TotalDenials,
    [property: JsonPropertyName("buckets")] IReadOnlyList<ConcurrencyTimeBucketDto> Buckets
);

/// <summary>
/// Enterprise True-Up audit report for procurement, contractual compliance and annual settlement.
/// Conforms to docs/02-kontext-a-ciele.md Goal G5 and docs/06-architektura.md Section 6.5.
/// </summary>
public sealed record TrueUpReportDto(
    [property: JsonPropertyName("tenantId")] string TenantId,
    [property: JsonPropertyName("licenseId")] string? LicenseId,
    [property: JsonPropertyName("productName")] string ProductName,
    [property: JsonPropertyName("licensedSeats")] int LicensedSeats,
    [property: JsonPropertyName("peakConcurrentSeats")] int PeakConcurrentSeats,
    [property: JsonPropertyName("overageBufferSeats")] int OverageBufferSeats,
    [property: JsonPropertyName("overageSeatsUsed")] int OverageSeatsUsed,
    [property: JsonPropertyName("contractCompliance")] string ContractCompliance,
    [property: JsonPropertyName("totalCheckouts")] int TotalCheckouts,
    [property: JsonPropertyName("totalDenials")] int TotalDenials,
    [property: JsonPropertyName("billableOverageSeatSeconds")] long BillableOverageSeatSeconds,
    [property: JsonPropertyName("periodStart")] DateTimeOffset PeriodStart,
    [property: JsonPropertyName("periodEnd")] DateTimeOffset PeriodEnd,
    [property: JsonPropertyName("generatedAtUtc")] DateTimeOffset GeneratedAtUtc
);

/// <summary>
/// Detail of an individual license denial event according to FLT-39.
/// </summary>
public sealed record DenialRecordDto(
    [property: JsonPropertyName("timestamp")] DateTimeOffset Timestamp,
    [property: JsonPropertyName("licenseId")] string LicenseId,
    [property: JsonPropertyName("subject")] string? Subject,
    [property: JsonPropertyName("reason")] string Reason,
    [property: JsonPropertyName("requestedFeature")] string? RequestedFeature,
    [property: JsonPropertyName("machineId")] string? MachineId
);

/// <summary>
/// Aggregated analytics of license denials for upsell and capacity right-sizing.
/// </summary>
public sealed record DenialsAnalyticsResponseDto(
    [property: JsonPropertyName("rangeStart")] DateTimeOffset RangeStart,
    [property: JsonPropertyName("rangeEnd")] DateTimeOffset RangeEnd,
    [property: JsonPropertyName("totalDenials")] int TotalDenials,
    [property: JsonPropertyName("uniqueSubjectsAffected")] int UniqueSubjectsAffected,
    [property: JsonPropertyName("denialsByReason")] IReadOnlyDictionary<string, int> DenialsByReason,
    [property: JsonPropertyName("recentDenials")] IReadOnlyList<DenialRecordDto> RecentDenials
);

/// <summary>
/// Cryptographic proof of immutable audit chain integrity according to FLT-37.
/// </summary>
public sealed record AuditVerificationProofDto(
    [property: JsonPropertyName("totalEventsVerified")] int TotalEventsVerified,
    [property: JsonPropertyName("firstEventId")] string? FirstEventId,
    [property: JsonPropertyName("lastEventId")] string? LastEventId,
    [property: JsonPropertyName("firstEventTimestamp")] DateTimeOffset? FirstEventTimestamp,
    [property: JsonPropertyName("lastEventTimestamp")] DateTimeOffset? LastEventTimestamp,
    [property: JsonPropertyName("rootHashHex")] string RootHashHex,
    [property: JsonPropertyName("isChainIntact")] bool IsChainIntact,
    [property: JsonPropertyName("tamperedEventId")] string? TamperedEventId,
    [property: JsonPropertyName("tamperReason")] string? TamperReason,
    [property: JsonPropertyName("verifiedAtUtc")] DateTimeOffset VerifiedAtUtc
);

/// <summary>
/// Detail of an active lease or seat in the cluster for real-time monitoring (symbolon top).
/// </summary>
public sealed record ActiveLeaseItemDto(
    [property: JsonPropertyName("seatId")] long SeatId,
    [property: JsonPropertyName("leaseId")] string LeaseId,
    [property: JsonPropertyName("licenseId")] string LicenseId,
    [property: JsonPropertyName("tenantId")] string TenantId,
    [property: JsonPropertyName("seatNo")] int SeatNo,
    [property: JsonPropertyName("holderFingerprint")] string? HolderFingerprint,
    [property: JsonPropertyName("machineId")] string? MachineId,
    [property: JsonPropertyName("userId")] string? UserId,
    [property: JsonPropertyName("acquiredAt")] DateTimeOffset? AcquiredAt,
    [property: JsonPropertyName("expiresAt")] DateTimeOffset? ExpiresAt,
    [property: JsonPropertyName("isBorrowed")] bool IsBorrowed,
    [property: JsonPropertyName("borrowedUntil")] DateTimeOffset? BorrowedUntil,
    [property: JsonPropertyName("productName")] string? ProductName
);

/// <summary>
/// Cluster-wide telemetry snapshot for real-time console monitor (symbolon top).
/// </summary>
public sealed record TopSystemStatsDto(
    [property: JsonPropertyName("serverTime")] DateTimeOffset ServerTime,
    [property: JsonPropertyName("uptimeSeconds")] double UptimeSeconds,
    [property: JsonPropertyName("activeLeases")] int ActiveLeases,
    [property: JsonPropertyName("totalCapacity")] int TotalCapacity,
    [property: JsonPropertyName("activeLicenses")] int ActiveLicenses,
    [property: JsonPropertyName("dlqDepth")] int DlqDepth,
    [property: JsonPropertyName("webhookFailures")] int WebhookFailures,
    [property: JsonPropertyName("auditEventsTotal")] long AuditEventsTotal,
    [property: JsonPropertyName("isAuditChainIntact")] bool IsAuditChainIntact,
    [property: JsonPropertyName("throughputSparkline")] IReadOnlyList<int> ThroughputSparkline
);

