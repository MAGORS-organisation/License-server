namespace Achilles.Domain.Grants;

/// <summary>
/// Inclusive seat range [From, To] where Count == To - From + 1 (GNT-3).
/// </summary>
public readonly record struct SeatRange(int From, int To)
{
    public int Count => To >= From ? To - From + 1 : 0;

    public bool OverlapsWith(SeatRange other) =>
        From <= other.To && other.From <= To;
}

/// <summary>
/// Cryptographic Disjunctive Seat Range Allocator ensuring non-overlapping intervals (GNT-3, GNT-4, GNT-5).
/// </summary>
public static class DisjunctiveGrantAllocator
{
    /// <summary>
    /// Allocates the first available non-overlapping contiguous seat range [from, to] of size <paramref name="requestedSeats"/>
    /// within [0, maxSeats - 1]. Returns null if not enough contiguous capacity is available.
    /// </summary>
    public static SeatRange? AllocateContiguousRange(
        int maxSeats,
        IReadOnlyList<SeatRange> activeRanges,
        int requestedSeats)
    {
        if (maxSeats <= 0 || requestedSeats <= 0 || requestedSeats > maxSeats)
        {
            return null;
        }

        // Sort active ranges by starting seat
        var sorted = activeRanges
            .Where(r => r.Count > 0)
            .OrderBy(r => r.From)
            .ToList();

        // Total active seats + requested must not exceed maxSeats (GNT-4)
        int currentActiveSeats = sorted.Sum(r => r.Count);
        if (currentActiveSeats + requestedSeats > maxSeats)
        {
            return null;
        }

        int currentStart = 0;

        foreach (var range in sorted)
        {
            if (range.From < currentStart)
            {
                // In case of overlapping active ranges, advance past the collision
                currentStart = Math.Max(currentStart, range.To + 1);
                continue;
            }

            // Check gap before this range
            int gap = range.From - currentStart;
            if (gap >= requestedSeats)
            {
                return new SeatRange(currentStart, currentStart + requestedSeats - 1);
            }

            currentStart = range.To + 1;
        }

        // Check trailing space after last range up to maxSeats - 1
        if (maxSeats - currentStart >= requestedSeats)
        {
            return new SeatRange(currentStart, currentStart + requestedSeats - 1);
        }

        return null;
    }
}
