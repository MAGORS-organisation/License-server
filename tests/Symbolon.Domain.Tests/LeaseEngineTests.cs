using FluentAssertions;
using Symbolon.Crypto;
using Symbolon.Protocol;
using Xunit;

namespace Symbolon.Domain.Tests;

public sealed class LeaseEngineTests
{
    private sealed class InMemorySeatStore : ISeatStore
    {
        public int TotalSeats { get; set; } = 2;
        private readonly List<SeatAllocation> _seats = [];
        private readonly Dictionary<string, SeatAllocation[]> _idempotent = [];

        public InMemorySeatStore(int count = 2)
        {
            TotalSeats = count;
            for (int i = 0; i < count; i++)
            {
                _seats.Add(new SeatAllocation
                {
                    SeatId = $"seat_{i}",
                    SeatNo = i,
                    LicenseId = "lic_test",
                    ExpiresAt = DateTimeOffset.MinValue,
                    LeaseSeq = 0
                });
            }
        }

        public Task<SeatAllocation?> TryAcquireOneAsync(string licenseId, string fingerprint, string? machineId, DateTimeOffset now, TimeSpan ttl, CancellationToken ct = default)
        {
            var seat = _seats.FirstOrDefault(s => s.LeaseId is null || s.ExpiresAt < now);
            if (seat is null) return Task.FromResult<SeatAllocation?>(null);

            string leaseId = $"lse_{Guid.NewGuid():N}";
            var allocated = seat with
            {
                LeaseId = leaseId,
                HolderFingerprint = fingerprint,
                MachineId = machineId,
                AcquiredAt = now,
                ExpiresAt = now + ttl,
                LeaseSeq = 0
            };
            int index = _seats.FindIndex(s => s.SeatId == seat.SeatId);
            _seats[index] = allocated;
            return Task.FromResult<SeatAllocation?>(allocated);
        }

        public Task<SeatAllocation[]?> TryAcquireManyAsync(string licenseId, string fingerprint, string? machineId, int quantity, DateTimeOffset now, TimeSpan ttl, CancellationToken ct = default)
        {
            var available = _seats.Where(s => s.LeaseId is null || s.ExpiresAt < now).Take(quantity).ToList();
            if (available.Count < quantity) return Task.FromResult<SeatAllocation[]?>(null);

            string leaseId = $"lse_{Guid.NewGuid():N}";
            var result = new List<SeatAllocation>();
            foreach (var seat in available)
            {
                var allocated = seat with
                {
                    LeaseId = leaseId,
                    HolderFingerprint = fingerprint,
                    MachineId = machineId,
                    AcquiredAt = now,
                    ExpiresAt = now + ttl,
                    LeaseSeq = 0
                };
                int index = _seats.FindIndex(s => s.SeatId == seat.SeatId);
                _seats[index] = allocated;
                result.Add(allocated);
            }
            return Task.FromResult<SeatAllocation[]?>(result.ToArray());
        }

        public Task<RenewOutcome> TryRenewAsync(string leaseId, string fingerprint, long clientSeq, DateTimeOffset now, TimeSpan ttl, TimeSpan resurrectionWindow, CancellationToken ct = default)
        {
            var seat = _seats.FirstOrDefault(s => s.LeaseId == leaseId);
            if (seat is null) return Task.FromResult(RenewOutcome.Unknown);
            if (seat.HolderFingerprint != fingerprint) return Task.FromResult(RenewOutcome.Conflict);
            if (seat.LeaseSeq != clientSeq) return Task.FromResult(RenewOutcome.SeqReplay);
            if (seat.ExpiresAt < now - resurrectionWindow) return Task.FromResult(RenewOutcome.Taken);

            var updated = seat with
            {
                ExpiresAt = now + ttl,
                LeaseSeq = seat.LeaseSeq + 1
            };
            int index = _seats.FindIndex(s => s.SeatId == seat.SeatId);
            _seats[index] = updated;
            return Task.FromResult(RenewOutcome.Renewed(updated));
        }

        public Task<bool> TryReleaseAsync(string leaseId, DateTimeOffset now, CancellationToken ct = default)
        {
            var seat = _seats.FirstOrDefault(s => s.LeaseId == leaseId);
            if (seat is null) return Task.FromResult(false);

            var updated = seat with
            {
                LeaseId = null,
                HolderFingerprint = null,
                ExpiresAt = now
            };
            int index = _seats.FindIndex(s => s.SeatId == seat.SeatId);
            _seats[index] = updated;
            return Task.FromResult(true);
        }

        public Task<SeatAllocation[]?> TryGetIdempotentAsync(string licenseId, string idempotencyKey, DateTimeOffset now, CancellationToken ct = default)
        {
            if (_idempotent.TryGetValue(idempotencyKey, out var allocs))
            {
                if (allocs[0].ExpiresAt > now) return Task.FromResult<SeatAllocation[]?>(allocs);
            }
            return Task.FromResult<SeatAllocation[]?>(null);
        }

        public Task SaveIdempotentAsync(string licenseId, string idempotencyKey, SeatAllocation[] allocations, DateTimeOffset now, TimeSpan ttl, CancellationToken ct = default)
        {
            _idempotent[idempotencyKey] = allocations;
            return Task.CompletedTask;
        }

        public Task<TimeSpan?> EstimateWaitAsync(string licenseId, DateTimeOffset now, CancellationToken ct = default)
        {
            return Task.FromResult<TimeSpan?>(TimeSpan.FromMinutes(2));
        }
    }

    private sealed class FakeTokenIssuer : ILeaseTokenIssuer
    {
        public string Issue(SeatAllocation allocation, IReadOnlyList<string>? entitlements = null) =>
            $"fake-token-seat-{allocation.SeatNo}-seq-{allocation.LeaseSeq}";

        public IReadOnlyList<string> Issue(IReadOnlyList<SeatAllocation> allocations, IReadOnlyList<string>? entitlements = null) =>
            allocations.Select(a => Issue(a, entitlements)).ToList();
    }

    [Fact]
    public async Task CheckoutAsync_WhenSeatsAvailable_Succeeds()
    {
        var store = new InMemorySeatStore(count: 2);
        var issuer = new FakeTokenIssuer();
        var audit = new InMemoryAuditLedger();
        var engine = new LeaseEngine(store, issuer, audit);

        var cmd = new CheckoutCommand(
            LicenseId: "lic_test",
            Fingerprint: "sha256:fp1",
            MachineId: "pc1",
            Quantity: 1,
            Features: ["core"],
            IdempotencyKey: null,
            AllowQueue: false,
            Ttl: TimeSpan.FromMinutes(10));

        var result = await engine.CheckoutAsync(cmd);

        result.IsSuccess.Should().BeTrue();
        result.Allocations.Should().HaveCount(1);
        result.Tokens.Should().HaveCount(1);
        result.Tokens![0].Should().Be("fake-token-seat-0-seq-0");
        audit.Events.Should().Contain(e => e.Type == "checkout");
    }

    [Fact]
    public async Task CheckoutAsync_WhenPoolExhausted_ReturnsPoolExhausted()
    {
        var store = new InMemorySeatStore(count: 1);
        var issuer = new FakeTokenIssuer();
        var audit = new InMemoryAuditLedger();
        var engine = new LeaseEngine(store, issuer, audit);

        // Checkout first seat
        await engine.CheckoutAsync(new CheckoutCommand("lic_test", "sha256:fp1", "pc1", 1, null, null, false, TimeSpan.FromMinutes(10)));

        // Checkout second seat
        var second = await engine.CheckoutAsync(new CheckoutCommand("lic_test", "sha256:fp2", "pc2", 1, null, null, false, TimeSpan.FromMinutes(10)));

        second.IsSuccess.Should().BeFalse();
        second.Reason.Should().Be("seat-pool-exhausted");
        audit.Events.Should().Contain(e => e.Type == "deny");
    }

    [Fact]
    public async Task CheckoutAsync_IdempotencyKey_ReturnsSameLease()
    {
        var store = new InMemorySeatStore(count: 2);
        var issuer = new FakeTokenIssuer();
        var audit = new InMemoryAuditLedger();
        var engine = new LeaseEngine(store, issuer, audit);

        string key = "req-unique-123";
        var cmd = new CheckoutCommand("lic_test", "sha256:fp1", "pc1", 1, null, key, false, TimeSpan.FromMinutes(10));

        var first = await engine.CheckoutAsync(cmd);
        var second = await engine.CheckoutAsync(cmd);

        first.IsSuccess.Should().BeTrue();
        second.IsSuccess.Should().BeTrue();
        first.Allocations![0].LeaseId.Should().Be(second.Allocations![0].LeaseId);
        first.Tokens![0].Should().Be(second.Tokens![0]);
    }

    [Fact]
    public async Task RenewAsync_WithCorrectSequence_IncrementsSequence()
    {
        var store = new InMemorySeatStore(count: 2);
        var issuer = new FakeTokenIssuer();
        var audit = new InMemoryAuditLedger();
        var engine = new LeaseEngine(store, issuer, audit);

        var checkout = await engine.CheckoutAsync(new CheckoutCommand("lic_test", "sha256:fp1", "pc1", 1, null, null, false, TimeSpan.FromMinutes(10)));
        string leaseId = checkout.Allocations![0].LeaseId!;

        var renew = await engine.RenewAsync(leaseId, "sha256:fp1", clientSeq: 0, ttl: TimeSpan.FromMinutes(10), resurrectionWindow: TimeSpan.FromMinutes(5));

        renew.IsSuccess.Should().BeTrue();
        renew.Allocation!.LeaseSeq.Should().Be(1);
        renew.Token.Should().Be("fake-token-seat-0-seq-1");
    }

    [Fact]
    public async Task RenewAsync_WithStaleSequence_ReturnsConflict()
    {
        var store = new InMemorySeatStore(count: 2);
        var issuer = new FakeTokenIssuer();
        var audit = new InMemoryAuditLedger();
        var engine = new LeaseEngine(store, issuer, audit);

        var checkout = await engine.CheckoutAsync(new CheckoutCommand("lic_test", "sha256:fp1", "pc1", 1, null, null, false, TimeSpan.FromMinutes(10)));
        string leaseId = checkout.Allocations![0].LeaseId!;

        // Attempt renew with wrong sequence
        var renew = await engine.RenewAsync(leaseId, "sha256:fp1", clientSeq: 999, ttl: TimeSpan.FromMinutes(10), resurrectionWindow: TimeSpan.FromMinutes(5));

        renew.IsSuccess.Should().BeFalse();
        renew.Reason.Should().Be("stale-sequence");
    }

    [Fact]
    public async Task ReleaseAsync_FreesSeatForNewClient()
    {
        var store = new InMemorySeatStore(count: 1);
        var issuer = new FakeTokenIssuer();
        var audit = new InMemoryAuditLedger();
        var engine = new LeaseEngine(store, issuer, audit);

        var checkout = await engine.CheckoutAsync(new CheckoutCommand("lic_test", "sha256:fp1", "pc1", 1, null, null, false, TimeSpan.FromMinutes(10)));
        string leaseId = checkout.Allocations![0].LeaseId!;

        bool released = await engine.ReleaseAsync(leaseId);
        released.Should().BeTrue();

        // New client should now be able to acquire the freed seat
        var second = await engine.CheckoutAsync(new CheckoutCommand("lic_test", "sha256:fp2", "pc2", 1, null, null, false, TimeSpan.FromMinutes(10)));
        second.IsSuccess.Should().BeTrue();
    }
}
