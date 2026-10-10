using FluentAssertions;
using Achilles.Domain.Replication;
using Achilles.Protocol.Replication;
using Xunit;

namespace Achilles.Domain.Tests;

public class GeoReplicationTests
{
    [Fact]
    public void VectorClock_Increment_And_Compare_DetectsDominanceAndConcurrency()
    {
        var clockA = VectorClock.Empty.Increment("eu-central-1");
        var clockB = VectorClock.Empty.Increment("us-east-1");

        // Concurrent evolution
        clockA.CompareToClock(clockB).Should().Be(VectorClockRelation.Concurrent);
        clockB.CompareToClock(clockA).Should().Be(VectorClockRelation.Concurrent);

        // Advance clockA further
        var clockA2 = clockA.Increment("eu-central-1");
        clockA2.CompareToClock(clockA).Should().Be(VectorClockRelation.Dominates);
        clockA.CompareToClock(clockA2).Should().Be(VectorClockRelation.DominatedBy);

        // Merge preserves maximums and dominates predecessors
        var merged = clockA2.Merge(clockB);
        merged.GetClock("eu-central-1").Should().Be(2);
        merged.GetClock("us-east-1").Should().Be(1);
        merged.CompareToClock(clockA2).Should().Be(VectorClockRelation.Dominates);
        merged.CompareToClock(clockB).Should().Be(VectorClockRelation.Dominates);
    }

    [Fact]
    public void PnCounter_Commutative_Associative_And_Idempotent()
    {
        var counterA = PnCounter.Empty.Increment("eu", 5).Decrement("eu", 2); // 3
        var counterB = PnCounter.Empty.Increment("us", 10).Decrement("us", 4); // 6
        var counterC = PnCounter.Empty.Increment("ap", 7).Decrement("ap", 1); // 6

        // Commutativity: A.Merge(B) == B.Merge(A)
        var ab = counterA.Merge(counterB);
        var ba = counterB.Merge(counterA);
        ab.Value.Should().Be(ba.Value);
        ab.Value.Should().Be(9);

        // Associativity: (A + B) + C == A + (B + C)
        var left = ab.Merge(counterC);
        var right = counterA.Merge(counterB.Merge(counterC));
        left.Value.Should().Be(right.Value);
        left.Value.Should().Be(15);

        // Idempotence: A.Merge(A) == A
        var selfMerged = counterA.Merge(counterA);
        selfMerged.Value.Should().Be(counterA.Value);
    }

    [Fact]
    public void ReplicationSecurity_Hmac_And_AntiReplay()
    {
        const string secret = "cluster-super-secret-key-12345";
        var now = DateTimeOffset.UtcNow;
        byte[] payload = ReplicationSecurity.CreateCanonicalPayload("eu-central-1", "us-east-1", "delta_001", now);

        string hmac = ReplicationSecurity.ComputeHmac(payload, secret);
        hmac.Should().NotBeNullOrWhiteSpace();

        // Valid signature passes
        ReplicationSecurity.VerifyHmac(payload, hmac, secret).Should().BeTrue();

        // Tampered payload fails
        byte[] tamperedPayload = ReplicationSecurity.CreateCanonicalPayload("eu-central-1", "us-east-1", "delta_002", now);
        ReplicationSecurity.VerifyHmac(tamperedPayload, hmac, secret).Should().BeFalse();

        // Wrong secret fails
        ReplicationSecurity.VerifyHmac(payload, hmac, "wrong-cluster-secret").Should().BeFalse();

        // Fresh timestamp passes
        ReplicationSecurity.IsTimestampFresh(now, now).Should().BeTrue();

        // Expired timestamp (10 minutes drift) fails default 5m window
        var pastTime = now.AddMinutes(-10);
        ReplicationSecurity.IsTimestampFresh(pastTime, now).Should().BeFalse();
    }

    [Fact]
    public void GeoReplicationEngine_DisjointAllocation_IndependentRegions()
    {
        var now = DateTimeOffset.UtcNow;
        var euEngine = new GeoReplicationEngine("eu-central-1", seatRangeStart: 1, seatRangeEnd: 10);
        var usEngine = new GeoReplicationEngine("us-east-1", seatRangeStart: 11, seatRangeEnd: 20);

        euEngine.RegisterPeer("us-east-1", "https://us.symbolon.internal/v1/replication", 11, 20);
        usEngine.RegisterPeer("eu-central-1", "https://eu.symbolon.internal/v1/replication", 1, 10);

        // EU allocates from 1..10
        var euSeat = euEngine.TryAllocateLocalSeat("lic_corp", "fp_eu_1", "mach_1", TimeSpan.FromMinutes(30), now);
        euSeat.Should().NotBeNull();
        euSeat!.SeatNo.Should().Be(1);

        // US allocates from 11..20
        var usSeat = usEngine.TryAllocateLocalSeat("lic_corp", "fp_us_1", "mach_2", TimeSpan.FromMinutes(30), now);
        usSeat.Should().NotBeNull();
        usSeat!.SeatNo.Should().Be(11);

        // Disjoint pools ensure zero seat collisions across independent regions
        euSeat.SeatNo.Should().NotBe(usSeat.SeatNo);
    }

    [Fact]
    public void GeoReplicationEngine_Sync_Assimilates_And_Converges_State()
    {
        var now = DateTimeOffset.UtcNow;
        var euEngine = new GeoReplicationEngine("eu-central-1", seatRangeStart: 1, seatRangeEnd: 10);
        var usEngine = new GeoReplicationEngine("us-east-1", seatRangeStart: 11, seatRangeEnd: 20);

        euEngine.RegisterPeer("us-east-1", "https://us.symbolon.internal", 11, 20);
        usEngine.RegisterPeer("eu-central-1", "https://eu.symbolon.internal", 1, 10);

        // Each region performs local checkouts
        var euSeat = euEngine.TryAllocateLocalSeat("lic_multi", "fp_eu", null, TimeSpan.FromMinutes(15), now);
        var usSeat = usEngine.TryAllocateLocalSeat("lic_multi", "fp_us", null, TimeSpan.FromMinutes(15), now);

        euSeat.Should().NotBeNull();
        usSeat.Should().NotBeNull();

        // EU only knows its own seat initially
        euEngine.GetGlobalActiveSeats("lic_multi", now).Should().HaveCount(1);
        usEngine.GetGlobalActiveSeats("lic_multi", now).Should().HaveCount(1);

        // EU sends delta to US
        var deltaEu = euEngine.GenerateDeltaForPeer("us-east-1", usEngine.CurrentClock, now);
        var syncResponseUs = usEngine.AssimilateRemoteDelta(deltaEu, now);
        syncResponseUs.Success.Should().BeTrue();

        // US sends delta to EU
        var deltaUs = usEngine.GenerateDeltaForPeer("eu-central-1", euEngine.CurrentClock, now);
        var syncResponseEu = euEngine.AssimilateRemoteDelta(deltaUs, now);
        syncResponseEu.Success.Should().BeTrue();

        // Both regions converge: each sees both active seats
        var euActive = euEngine.GetGlobalActiveSeats("lic_multi", now);
        var usActive = usEngine.GetGlobalActiveSeats("lic_multi", now);

        euActive.Should().HaveCount(2);
        usActive.Should().HaveCount(2);
        euActive.Select(s => s.SeatNo).Should().BeEquivalentTo([1, 11]);
        usActive.Select(s => s.SeatNo).Should().BeEquivalentTo([1, 11]);
    }

    [Fact]
    public void GeoReplicationEngine_PartitionHealing_SplitBrainResilience()
    {
        var now = DateTimeOffset.UtcNow;
        var euEngine = new GeoReplicationEngine("eu-central-1", seatRangeStart: 1, seatRangeEnd: 5);
        var usEngine = new GeoReplicationEngine("us-east-1", seatRangeStart: 6, seatRangeEnd: 10);

        // Network partition: regions run isolated
        var euSeat1 = euEngine.TryAllocateLocalSeat("lic_partition", "fp_1", null, TimeSpan.FromMinutes(30), now);
        var euSeat2 = euEngine.TryAllocateLocalSeat("lic_partition", "fp_2", null, TimeSpan.FromMinutes(30), now);
        euEngine.TryReleaseLocalSeat(euSeat1!.LeaseId!, now);

        var usSeat6 = usEngine.TryAllocateLocalSeat("lic_partition", "fp_6", null, TimeSpan.FromMinutes(30), now);
        var usSeat7 = usEngine.TryAllocateLocalSeat("lic_partition", "fp_7", null, TimeSpan.FromMinutes(30), now);

        // Local state before partition heals
        euEngine.GetGlobalActiveSeats("lic_partition", now).Should().HaveCount(1); // seat 2 active, seat 1 released
        usEngine.GetGlobalActiveSeats("lic_partition", now).Should().HaveCount(2); // seats 6, 7 active

        // Partition heals: bilateral sync
        var euToUsDelta = euEngine.GenerateDeltaForPeer("us-east-1", peerClock: null, now);
        usEngine.AssimilateRemoteDelta(euToUsDelta, now);

        var usToEuDelta = usEngine.GenerateDeltaForPeer("eu-central-1", peerClock: null, now);
        euEngine.AssimilateRemoteDelta(usToEuDelta, now);

        // Both converged on identical active set: seats 2, 6, 7
        var euGlobal = euEngine.GetGlobalActiveSeats("lic_partition", now);
        var usGlobal = usEngine.GetGlobalActiveSeats("lic_partition", now);

        euGlobal.Should().HaveCount(3);
        usGlobal.Should().HaveCount(3);
        euGlobal.Select(s => s.SeatNo).Should().BeEquivalentTo([2, 6, 7]);
        usGlobal.Select(s => s.SeatNo).Should().BeEquivalentTo([2, 6, 7]);
    }
}
