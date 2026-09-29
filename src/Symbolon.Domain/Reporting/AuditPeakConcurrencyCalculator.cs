using System;
using System.Collections.Generic;
using System.Linq;
using Symbolon.Protocol.Reporting;

namespace Symbolon.Domain.Reporting;

/// <summary>
/// Event descriptor used to reconstruct the exact concurrency timeline.
/// Conforms to spec/07-floating-protokol.md FLT-38.
/// </summary>
public sealed record ConcurrencyAuditEvent(
    string Id,
    DateTimeOffset Timestamp,
    string Type,
    string? LicenseId,
    string? Subject,
    int Quantity = 1
);

/// <summary>
/// Exact peak and average concurrency calculator derived strictly from the append-only audit event chain.
/// Never uses statistical sampling; calculates exact step-function timeline.
/// </summary>
public static class AuditPeakConcurrencyCalculator
{
    public static ConcurrencyAnalyticsResponseDto Calculate(
        IReadOnlyList<ConcurrencyAuditEvent> events,
        DateTimeOffset rangeStart,
        DateTimeOffset rangeEnd,
        string bucketSize,
        string? licenseId,
        int licensedCapacity,
        int initialActiveSeats = 0)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(bucketSize);

        if (rangeEnd <= rangeStart)
        {
            rangeEnd = rangeStart.AddDays(1);
        }

        var sortedEvents = events
            .OrderBy(e => e.Timestamp)
            .ThenBy(e => e.Id)
            .ToList();

        // 1. Establish discrete bucket boundaries
        var boundaries = GenerateBucketBoundaries(rangeStart, rangeEnd, bucketSize);
        var bucketResults = new List<ConcurrencyTimeBucketDto>();

        int currentSeats = Math.Max(0, initialActiveSeats);
        int overallPeak = currentSeats;
        int totalCheckoutsAll = 0;
        int totalDenialsAll = 0;

        int eventIndex = 0;

        for (int b = 0; b < boundaries.Count - 1; b++)
        {
            DateTimeOffset bucketStart = boundaries[b];
            DateTimeOffset bucketEnd = boundaries[b + 1];
            double bucketDurationSeconds = (bucketEnd - bucketStart).TotalSeconds;

            int bucketPeak = currentSeats;
            int bucketCheckouts = 0;
            int bucketDenials = 0;
            double areaUnderCurveSeconds = 0.0;
            DateTimeOffset lastTimestamp = bucketStart;

            while (eventIndex < sortedEvents.Count && sortedEvents[eventIndex].Timestamp < bucketEnd)
            {
                var ev = sortedEvents[eventIndex];

                if (ev.Timestamp >= bucketStart)
                {
                    // Accumulate time spent at currentSeats level before this event
                    double elapsed = (ev.Timestamp - lastTimestamp).TotalSeconds;
                    if (elapsed > 0)
                    {
                        areaUnderCurveSeconds += currentSeats * elapsed;
                        lastTimestamp = ev.Timestamp;
                    }
                }

                // Apply state transition
                if (string.Equals(ev.Type, "checkout", StringComparison.OrdinalIgnoreCase))
                {
                    currentSeats += Math.Max(1, ev.Quantity);
                    if (ev.Timestamp >= bucketStart)
                    {
                        bucketCheckouts++;
                        totalCheckoutsAll++;
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
                    if (ev.Timestamp >= bucketStart)
                    {
                        bucketDenials++;
                        totalDenialsAll++;
                    }
                }

                if (ev.Timestamp >= bucketStart)
                {
                    if (currentSeats > bucketPeak)
                    {
                        bucketPeak = currentSeats;
                    }
                    if (currentSeats > overallPeak)
                    {
                        overallPeak = currentSeats;
                    }
                }

                eventIndex++;
            }

            // Remainder of bucket duration
            double remainingSeconds = (bucketEnd - lastTimestamp).TotalSeconds;
            if (remainingSeconds > 0)
            {
                areaUnderCurveSeconds += currentSeats * remainingSeconds;
            }

            double averageSeats = bucketDurationSeconds > 0 ? areaUnderCurveSeconds / bucketDurationSeconds : currentSeats;
            double peakUtil = licensedCapacity > 0 ? Math.Round((double)bucketPeak / licensedCapacity * 100.0, 1) : 0.0;

            bucketResults.Add(new ConcurrencyTimeBucketDto(
                bucketStart,
                bucketPeak,
                Math.Round(averageSeats, 2),
                bucketCheckouts,
                bucketDenials,
                licensedCapacity,
                peakUtil
            ));
        }

        double overallPeakUtil = licensedCapacity > 0 ? Math.Round((double)overallPeak / licensedCapacity * 100.0, 1) : 0.0;

        return new ConcurrencyAnalyticsResponseDto(
            rangeStart,
            rangeEnd,
            bucketSize.ToUpperInvariant(),
            licenseId,
            licensedCapacity,
            overallPeak,
            overallPeakUtil,
            totalCheckoutsAll,
            totalDenialsAll,
            bucketResults
        );
    }

    private static List<DateTimeOffset> GenerateBucketBoundaries(DateTimeOffset start, DateTimeOffset end, string bucketSize)
    {
        var boundaries = new List<DateTimeOffset>();
        DateTimeOffset current = start;

        while (current < end)
        {
            boundaries.Add(current);
            if (string.Equals(bucketSize, "hour", StringComparison.OrdinalIgnoreCase))
            {
                current = current.AddHours(1);
            }
            else if (string.Equals(bucketSize, "day", StringComparison.OrdinalIgnoreCase))
            {
                current = current.AddDays(1);
            }
            else if (string.Equals(bucketSize, "week", StringComparison.OrdinalIgnoreCase))
            {
                current = current.AddDays(7);
            }
            else if (string.Equals(bucketSize, "month", StringComparison.OrdinalIgnoreCase))
            {
                current = current.AddMonths(1);
            }
            else
            {
                current = current.AddDays(1);
            }
        }

        boundaries.Add(end);
        return boundaries;
    }
}
