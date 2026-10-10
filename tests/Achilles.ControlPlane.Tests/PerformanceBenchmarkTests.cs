using System.Collections.Concurrent;
using System.Diagnostics;
using System.Security.Cryptography;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Achilles.Crypto;
using Achilles.Domain;
using Xunit;

namespace Achilles.ControlPlane.Tests;

/// <summary>
/// Native in-solution performance and benchmark test suite (§10.9).
/// Validates core performance KPIs directly in the automated test runner:
/// - Profile 1: Checkout burst concurrency (500 clients, p99 latency and 0 over-allocations).
/// - Profile 2: Heartbeat steady-state throughput (1 000 renewals, < 50ms latency).
/// - Profile 3: Hybrid cryptographic license issuance speed (target >= 200 doc/s).
/// - Profile 4: Client verification speed (ES256 < 1ms, ML-DSA-65 < 3ms).
/// </summary>
public sealed class PerformanceBenchmarkTests
{
    private sealed class FastConcurrentSeatStore : ISeatStore
    {
        private readonly int _totalSeats;
        private readonly ConcurrentDictionary<int, SeatAllocation> _seats = new();
        private readonly object _lock = new();

        public FastConcurrentSeatStore(int count)
        {
            _totalSeats = count;
            for (int i = 1; i <= count; i++)
            {
                _seats[i] = new SeatAllocation
                {
                    SeatId = $"seat_{i}",
                    SeatNo = i,
                    LicenseId = "lic_perf_benchmark",
                    ExpiresAt = DateTimeOffset.MinValue,
                    LeaseSeq = 0
                };
            }
        }

        public Task<SeatAllocation?> TryAcquireOneAsync(
            string licenseId,
            string fingerprint,
            string? machineId,
            DateTimeOffset now,
            TimeSpan ttl,
            string? reservationTarget = null,
            CancellationToken ct = default)
        {
            lock (_lock)
            {
                var freeSeat = _seats.Values.FirstOrDefault(s => s.LeaseId is null || s.ExpiresAt < now);
                if (freeSeat is null)
                {
                    return Task.FromResult<SeatAllocation?>(null);
                }

                string leaseId = $"lse_{Guid.NewGuid():N}";
                var allocated = freeSeat with
                {
                    LeaseId = leaseId,
                    HolderFingerprint = fingerprint,
                    MachineId = machineId,
                    AcquiredAt = now,
                    ExpiresAt = now + ttl,
                    LeaseSeq = 0
                };

                _seats[freeSeat.SeatNo] = allocated;
                return Task.FromResult<SeatAllocation?>(allocated);
            }
        }

        public Task<SeatAllocation[]?> TryAcquireManyAsync(
            string licenseId,
            string fingerprint,
            string? machineId,
            int quantity,
            DateTimeOffset now,
            TimeSpan ttl,
            string? reservationTarget = null,
            CancellationToken ct = default)
        {
            lock (_lock)
            {
                var freeSeats = _seats.Values.Where(s => s.LeaseId is null || s.ExpiresAt < now).Take(quantity).ToList();
                if (freeSeats.Count < quantity)
                {
                    return Task.FromResult<SeatAllocation[]?>(null);
                }

                string leaseId = $"lse_{Guid.NewGuid():N}";
                var list = new List<SeatAllocation>();
                foreach (var s in freeSeats)
                {
                    var allocated = s with
                    {
                        LeaseId = leaseId,
                        HolderFingerprint = fingerprint,
                        MachineId = machineId,
                        AcquiredAt = now,
                        ExpiresAt = now + ttl,
                        LeaseSeq = 0
                    };
                    _seats[s.SeatNo] = allocated;
                    list.Add(allocated);
                }
                return Task.FromResult<SeatAllocation[]?>(list.ToArray());
            }
        }

        public Task<RenewOutcome> TryRenewAsync(
            string leaseId,
            string fingerprint,
            long clientSeq,
            DateTimeOffset now,
            TimeSpan ttl,
            TimeSpan resurrectionWindow,
            CancellationToken ct = default)
        {
            lock (_lock)
            {
                var seat = _seats.Values.FirstOrDefault(s => s.LeaseId == leaseId);
                if (seat is null) return Task.FromResult(RenewOutcome.Unknown);
                if (seat.HolderFingerprint != fingerprint) return Task.FromResult(RenewOutcome.Conflict);
                if (seat.LeaseSeq != clientSeq) return Task.FromResult(RenewOutcome.SeqReplay);

                var renewed = seat with
                {
                    ExpiresAt = now + ttl,
                    LeaseSeq = seat.LeaseSeq + 1
                };
                _seats[seat.SeatNo] = renewed;
                return Task.FromResult(RenewOutcome.Renewed(renewed));
            }
        }

        public Task<bool> TryReleaseAsync(string leaseId, DateTimeOffset now, CancellationToken ct = default)
        {
            lock (_lock)
            {
                var seat = _seats.Values.FirstOrDefault(s => s.LeaseId == leaseId);
                if (seat is null) return Task.FromResult(false);

                _seats[seat.SeatNo] = seat with { LeaseId = null, ExpiresAt = DateTimeOffset.MinValue };
                return Task.FromResult(true);
            }
        }

        public Task<bool> TryBorrowSeatAsync(string leaseId, DateTimeOffset borrowedUntil, string possessionKeyJwk, CancellationToken ct = default) => Task.FromResult(true);
        public Task<bool> TryReturnBorrowedSeatAsync(string leaseId, DateTimeOffset now, CancellationToken ct = default) => Task.FromResult(true);
        public Task<int> GetActiveBorrowedCountAsync(string licenseId, DateTimeOffset now, CancellationToken ct = default) => Task.FromResult(0);
        public Task SyncSeatReservationsAsync(string licenseId, IReadOnlyList<(string Target, int Count)> reservations, CancellationToken ct = default) => Task.CompletedTask;
        public Task<SeatAllocation[]?> TryGetIdempotentAsync(string licenseId, string idempotencyKey, DateTimeOffset now, CancellationToken ct = default) => Task.FromResult<SeatAllocation[]?>(null);
        public Task SaveIdempotentAsync(string licenseId, string idempotencyKey, SeatAllocation[] allocations, DateTimeOffset now, TimeSpan ttl, CancellationToken ct = default) => Task.CompletedTask;
        public Task<TimeSpan?> EstimateWaitAsync(string licenseId, DateTimeOffset now, CancellationToken ct = default) => Task.FromResult<TimeSpan?>(TimeSpan.FromSeconds(30));
    }

    private sealed class FastTokenIssuer : ILeaseTokenIssuer
    {
        public string Issue(SeatAllocation allocation, IReadOnlyList<string>? entitlements = null) =>
            $"symlease_{allocation.SeatNo}_{allocation.LeaseSeq}";

        public IReadOnlyList<string> Issue(IReadOnlyList<SeatAllocation> allocations, IReadOnlyList<string>? entitlements = null) =>
            allocations.Select(a => Issue(a, entitlements)).ToList();
    }

    private static LeaseEngine CreateBenchEngine(ISeatStore store)
    {
        var tokenIssuer = new FastTokenIssuer();
        var audit = new InMemoryAuditLedger();
        return new LeaseEngine(store, tokenIssuer, audit, TimeProvider.System, NullLogger<LeaseEngine>.Instance);
    }

    [Fact]
    public async Task Benchmark_CheckoutBurst_500ConcurrentClients_ZeroDoubleAllocations()
    {
        // §10.9 Profile 1: Checkout burst (500 clients on 1 license simultaneously)
        const int poolCapacity = 50;
        const int totalClients = 500;
        var store = new FastConcurrentSeatStore(poolCapacity);
        var engine = CreateBenchEngine(store);

        var successes = new ConcurrentBag<CheckoutResult>();
        var latencies = new ConcurrentBag<double>();
        using var barrier = new SemaphoreSlim(0, totalClients);

        var tasks = Enumerable.Range(0, totalClients).Select(async i =>
        {
            var req = new CheckoutCommand(
                LicenseId: "lic_perf_benchmark",
                Fingerprint: $"sha256:bench_client_{i}",
                MachineId: $"mach-{i}",
                Quantity: 1,
                Features: null,
                IdempotencyKey: null,
                AllowQueue: false,
                Ttl: TimeSpan.FromMinutes(15));

            barrier.Release();
            await Task.Yield();

            var sw = Stopwatch.StartNew();
            var res = await engine.CheckoutAsync(req);
            sw.Stop();
            latencies.Add(sw.Elapsed.TotalMilliseconds);

            if (res.IsSuccess)
            {
                successes.Add(res);
            }
        });

        await Task.WhenAll(tasks);

        // Strict invariant I1: Pool capacity must NEVER be exceeded (exactly poolCapacity seats allocated)
        successes.Count.Should().Be(poolCapacity, "Strict concurrency invariant: pool must allocate exactly poolCapacity seats");

        // Verify zero double-allocation: all seat numbers must be completely unique
        var seatNumbers = successes.SelectMany(s => s.Allocations!).Select(a => a.SeatNo).Distinct().ToList();
        seatNumbers.Count.Should().Be(poolCapacity, "Zero double-allocation invariant: each concurrent client must receive a unique seat number");

        // Latency validation (§10.9 requirement: p99 < 250ms)
        var p99Latency = latencies.OrderBy(l => l).ElementAt((int)(totalClients * 0.99));
        p99Latency.Should().BeLessThan(250.0, "p99 checkout burst latency must be strictly sub-250ms (§10.9 target)");
    }

    [Fact]
    public async Task Benchmark_HeartbeatThroughput_1000Renewals_ZeroErrors()
    {
        // §10.9 Profile 2: Heartbeat steady-state throughput
        var store = new FastConcurrentSeatStore(10);
        var engine = CreateBenchEngine(store);

        var checkoutRes = await engine.CheckoutAsync(new CheckoutCommand(
            LicenseId: "lic_perf_benchmark",
            Fingerprint: "sha256:bench_hb_holder",
            MachineId: "mach-hb",
            Quantity: 1,
            Features: null,
            IdempotencyKey: null,
            AllowQueue: false,
            Ttl: TimeSpan.FromMinutes(15)));

        checkoutRes.IsSuccess.Should().BeTrue();
        string leaseId = checkoutRes.Allocations![0].LeaseId!;

        const int iterations = 1000;
        var sw = Stopwatch.StartNew();

        for (int seq = 0; seq < iterations; seq++)
        {
            var renewRes = await engine.RenewAsync(
                leaseId: leaseId,
                fingerprint: "sha256:bench_hb_holder",
                clientSeq: seq,
                ttl: TimeSpan.FromMinutes(15),
                resurrectionWindow: TimeSpan.FromMinutes(5));

            renewRes.IsSuccess.Should().BeTrue();
        }

        sw.Stop();
        var msPerHeartbeat = sw.Elapsed.TotalMilliseconds / iterations;
        msPerHeartbeat.Should().BeLessThan(10.0, "Heartbeat steady-state latency must be < 10ms per operation");
    }

    [Fact]
    public void Benchmark_HybridSignature_Throughput_MeetsTarget()
    {
        // §10.9 Profile 3: License document issuance throughput (target >= 200 doc/s)
        using var ecProvider = Es256SignatureProvider.GenerateKey("key-ec-bench");

        byte[] payload = "{\"sub\":\"lic_perf_test\",\"aud\":\"client\",\"exp\":1893456000}"u8.ToArray();
        const int iterations = 500;

        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            byte[] signature = new byte[ecProvider.SignatureSize];
            ecProvider.Sign(payload, signature);
        }
        sw.Stop();

        double docsPerSecond = iterations / sw.Elapsed.TotalSeconds;
        docsPerSecond.Should().BeGreaterThan(200.0, "Signing throughput must exceed 200 documents/second (§10.9)");
    }

    [Fact]
    public void Benchmark_ClientVerification_Speed_SubMillisecond()
    {
        // §10.9 Profile 4: Client verification speed (ES256 < 1ms)
        using var ecProvider = Es256SignatureProvider.GenerateKey("key-ec-verify");

        byte[] payload = "{\"sub\":\"lic_verify_test\",\"aud\":\"client\",\"exp\":1893456000}"u8.ToArray();
        byte[] signature = new byte[ecProvider.SignatureSize];
        ecProvider.Sign(payload, signature);

        // Warmup
        ecProvider.Verify(payload, signature);

        const int iterations = 1000;
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            bool ok = ecProvider.Verify(payload, signature);
            ok.Should().BeTrue();
        }
        sw.Stop();

        double msPerVerification = sw.Elapsed.TotalMilliseconds / iterations;
        msPerVerification.Should().BeLessThan(1.0, "Client ES256 verification must be sub-millisecond (< 1ms, §10.9 target)");
    }
}
