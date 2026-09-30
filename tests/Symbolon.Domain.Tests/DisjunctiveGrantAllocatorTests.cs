using FluentAssertions;
using Symbolon.Domain.Grants;
using Xunit;

namespace Symbolon.Domain.Tests;

public sealed class DisjunctiveGrantAllocatorTests
{
    [Fact]
    public void AllocateContiguousRange_EmptyActiveRanges_AllocatesFromZero()
    {
        var range = DisjunctiveGrantAllocator.AllocateContiguousRange(10, [], 4);

        range.Should().NotBeNull();
        range!.Value.From.Should().Be(0);
        range.Value.To.Should().Be(3);
        range.Value.Count.Should().Be(4);
    }

    [Fact]
    public void AllocateContiguousRange_ExistingAtStart_AllocatesImmediatelyAfter()
    {
        var active = new List<SeatRange> { new(0, 3) }; // 4 seats taken [0..3]
        var range = DisjunctiveGrantAllocator.AllocateContiguousRange(10, active, 3);

        range.Should().NotBeNull();
        range!.Value.From.Should().Be(4);
        range.Value.To.Should().Be(6);
        range.Value.Count.Should().Be(3);
    }

    [Fact]
    public void AllocateContiguousRange_ExistingAtEnd_AllocatesInLeadingGap()
    {
        var active = new List<SeatRange> { new(5, 9) }; // seats [5..9] taken
        var range = DisjunctiveGrantAllocator.AllocateContiguousRange(10, active, 5);

        range.Should().NotBeNull();
        range!.Value.From.Should().Be(0);
        range.Value.To.Should().Be(4);
    }

    [Fact]
    public void AllocateContiguousRange_MiddleGap_AllocatesBetweenGrants()
    {
        var active = new List<SeatRange>
        {
            new(0, 1), // [0..1] (2 seats)
            new(6, 9)  // [6..9] (4 seats)
        };

        // Gap is [2..5] (4 seats)
        var range = DisjunctiveGrantAllocator.AllocateContiguousRange(10, active, 4);

        range.Should().NotBeNull();
        range!.Value.From.Should().Be(2);
        range.Value.To.Should().Be(5);
    }

    [Fact]
    public void AllocateContiguousRange_ExceedsMaxSeats_ReturnsNull()
    {
        var active = new List<SeatRange>
        {
            new(0, 4), // 5 seats
            new(5, 7)  // 3 seats (total 8)
        };

        // maxSeats = 10, current = 8, requesting 3 -> 8 + 3 = 11 > 10 (GNT-4)
        var range = DisjunctiveGrantAllocator.AllocateContiguousRange(10, active, 3);
        range.Should().BeNull();
    }

    [Fact]
    public void AllocateContiguousRange_FragmentedGapsTooSmall_ReturnsNull()
    {
        var active = new List<SeatRange>
        {
            new(2, 3), // [2..3]
            new(6, 7)  // [6..7]
        };
        // Total seats = 10. Gaps: [0..1] (size 2), [4..5] (size 2), [8..9] (size 2).
        // Total free = 6, but max contiguous gap is 2.
        // Requesting 3 contiguous seats (GNT-3) must fail.
        var range = DisjunctiveGrantAllocator.AllocateContiguousRange(10, active, 3);
        range.Should().BeNull();
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(10, 0)]
    [InlineData(10, -1)]
    [InlineData(5, 6)]
    public void AllocateContiguousRange_InvalidInputs_ReturnsNull(int maxSeats, int requested)
    {
        var range = DisjunctiveGrantAllocator.AllocateContiguousRange(maxSeats, [], requested);
        range.Should().BeNull();
    }

    [Fact]
    public void SeatRange_OverlapsWith_DetectsCollisions()
    {
        var r1 = new SeatRange(0, 4);
        var r2 = new SeatRange(4, 8); // Overlaps on 4
        var r3 = new SeatRange(5, 9); // No overlap with r1

        r1.OverlapsWith(r2).Should().BeTrue();
        r2.OverlapsWith(r1).Should().BeTrue();
        r1.OverlapsWith(r3).Should().BeFalse();
        r3.OverlapsWith(r1).Should().BeFalse();
    }
}
