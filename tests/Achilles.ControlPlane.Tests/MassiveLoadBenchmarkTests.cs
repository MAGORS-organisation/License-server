using System.Collections.Concurrent;
using System.Diagnostics;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Achilles.Crypto;
using Achilles.Domain;
using Xunit;
using Xunit.Abstractions;

namespace Achilles.ControlPlane.Tests;

/// <summary>
/// Massive Concurrent Load & Stress Benchmark Suite (§10.9 Goal 4).
/// Simulates 10,000 concurrent clients executing floating checkout, heartbeat renewals,
/// and cryptographic post-quantum verification to validate:
/// - O(1) allocation throughput under massive concurrency.
/// - Zero double-allocations and strict pool invariant preservation.
/// - p95/p99 latency distribution under 10k client load.
/// - ML-DSA-65 / ES256 hybrid signature throughput.
/// </summary>
public sealed class MassiveLoadBenchmarkTests
{
    private readonly ITestOutputHelper _output;

    public MassiveLoadBenchmarkTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private sealed class HighThroughputSeatStore : ISeatStore
    {
        private readonly int _capacity;
        private readonly ConcurrentDictionary<int, SeatAllocation> _seats = new();
        private readonly object _syncLock = new();

        public HighThroughputSeatStore(int capacity)
        {
            _capacity = capacity;
            for (int i = 1; i <= capacity; i++)
            {
                _seats[i] = new SeatAllocation
                {
                    SeatId = $"seat_10k_{i}",
                    SeatNo = i,
                    LicenseId = "lic_massive_10k",
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
            lock (_syncLock)
            {
                var candidate = _seats.Values.FirstOrDefault(s => s.LeaseId is null || s.ExpiresAt < now);
                if (candidate is null)
                {
                    return Task.FromResult<SeatAllocation?>(null);
                }

                string leaseId = $"lease_{Guid.NewGuid():N}";
                var allocated = candidate with
                {
                    LeaseId = leaseId,
                    HolderFingerprint = fingerprint,
                    MachineId = machineId,
                    AcquiredAt = now,
                    ExpiresAt = now + ttl,
                    LeaseSeq = 0
                };

                _seats[candidate.SeatNo] = allocated;
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
            lock (_syncLock)
            {
                var candidates = _seats.Values.Where(s => s.LeaseId is null || s.ExpiresAt < now).Take(quantity).ToList();
                if (candidates.Count < quantity)
                {
                    return Task.FromResult<SeatAllocation[]?>(null);
                }

                string leaseId = $"lease_{Guid.NewGuid():N}";
                var result = new List<SeatAllocation>(quantity);
                foreach (var c in candidates)
                {
                    var allocated = c with
                    {
                        LeaseId = leaseId,
                        HolderFingerprint = fingerprint,
                        MachineId = machineId,
                        AcquiredAt = now,
                        ExpiresAt = now + ttl,
                        LeaseSeq = 0
                    };
                    _seats[c.SeatNo] = allocated;
                    result.Add(allocated);
                }

                return Task.FromResult<SeatAllocation[]?>(result.ToArray());
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
            lock (_syncLock)
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
            lock (_syncLock)
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
        public Task<TimeSpan?> EstimateWaitAsync(string licenseId, DateTimeOffset now, CancellationToken ct = default) => Task.FromResult<TimeSpan?>(TimeSpan.FromSeconds(15));
    }

    private sealed class FastLeaseTokenIssuer : ILeaseTokenIssuer
    {
        public string Issue(SeatAllocation allocation, IReadOnlyList<string>? entitlements = null) =>
            $"achilles_lease_{allocation.SeatNo}_{allocation.LeaseSeq}_{allocation.LeaseId}";

        public IReadOnlyList<string> Issue(IReadOnlyList<SeatAllocation> allocations, IReadOnlyList<string>? entitlements = null) =>
            allocations.Select(a => Issue(a, entitlements)).ToList();
    }

    [Fact]
    public async Task MassiveBenchmark_10000ConcurrentClients_AllocationThroughputAndZeroCollisions()
    {
        const int poolCapacity = 2500;
        const int totalClients = 10000;

        var store = new HighThroughputSeatStore(poolCapacity);
        var tokenIssuer = new FastLeaseTokenIssuer();
        var audit = new InMemoryAuditLedger();
        var engine = new LeaseEngine(store, tokenIssuer, audit, TimeProvider.System, NullLogger<LeaseEngine>.Instance);

        var grantedLeases = new ConcurrentBag<CheckoutResult>();
        var deniedCount = 0;
        var latenciesMs = new ConcurrentBag<double>();

        var totalWatch = Stopwatch.StartNew();

        // Run across all available CPU threads in parallel batches
        await Parallel.ForEachAsync(
            Enumerable.Range(0, totalClients),
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 4 },
            async (i, ct) =>
            {
                var req = new CheckoutCommand(
                    LicenseId: "lic_massive_10k",
                    Fingerprint: $"sha256:client_10k_{i:D6}",
                    MachineId: $"WS-NODE-{i:D5}",
                    Quantity: 1,
                    Features: null,
                    IdempotencyKey: null,
                    AllowQueue: false,
                    Ttl: TimeSpan.FromMinutes(30));

                var sw = Stopwatch.StartNew();
                var result = await engine.CheckoutAsync(req, ct).ConfigureAwait(false);
                sw.Stop();

                latenciesMs.Add(sw.Elapsed.TotalMilliseconds);

                if (result.IsSuccess)
                {
                    grantedLeases.Add(result);
                }
                else
                {
                    Interlocked.Increment(ref deniedCount);
                }
            });

        totalWatch.Stop();

        // 1. Invariant verification
        grantedLeases.Count.Should().Be(poolCapacity, $"Strict invariant: exactly {poolCapacity} seats must be allocated out of {totalClients} concurrent requests");
        deniedCount.Should().Be(totalClients - poolCapacity, $"Strict rejection invariant: exactly {totalClients - poolCapacity} requests must be denied");

        // 2. Collision verification: all seat numbers must be completely unique [1..poolCapacity]
        var seatNumbers = grantedLeases.SelectMany(l => l.Allocations!).Select(a => a.SeatNo).Distinct().ToList();
        seatNumbers.Count.Should().Be(poolCapacity, "Zero collision invariant: every allocated seat must have a distinct seat number");

        // 3. Statistical Latency analysis
        var sortedLatencies = latenciesMs.OrderBy(l => l).ToList();
        double p50 = sortedLatencies[(int)(totalClients * 0.50)];
        double p90 = sortedLatencies[(int)(totalClients * 0.90)];
        double p95 = sortedLatencies[(int)(totalClients * 0.95)];
        double p99 = sortedLatencies[(int)(totalClients * 0.99)];
        double throughputRps = totalClients / totalWatch.Elapsed.TotalSeconds;

        _output.WriteLine($"[10,000 CLIENT BENCHMARK REPORT]");
        _output.WriteLine($"Total Requests:       {totalClients:N0}");
        _output.WriteLine($"Allocated Seats:      {grantedLeases.Count:N0} (Pool: {poolCapacity:N0})");
        _output.WriteLine($"Denied Requests:     {deniedCount:N0}");
        _output.WriteLine($"Elapsed Time:         {totalWatch.Elapsed.TotalMilliseconds:F1} ms");
        _output.WriteLine($"Throughput:           {throughputRps:F0} ops/sec");
        _output.WriteLine($"Latency p50:          {p50:F3} ms");
        _output.WriteLine($"Latency p90:          {p90:F3} ms");
        _output.WriteLine($"Latency p95:          {p95:F3} ms");
        _output.WriteLine($"Latency p99:          {p99:F3} ms");

        p50.Should().BeLessThan(10.0, "Median latency must be strictly sub-10ms");
        p99.Should().BeLessThan(100.0, "p99 latency under 10k burst must remain sub-100ms");
        throughputRps.Should().BeGreaterThan(1000.0, "Throughput must exceed 1,000 allocations/sec under high concurrency");
    }

    [Fact]
    public async Task MassiveBenchmark_10000Heartbeats_ThroughputAndSequenceMonotonicity()
    {
        const int activeLeasesCount = 100;
        const int renewalsPerLease = 100; // 100 * 100 = 10,000 total heartbeats
        const int totalHeartbeats = activeLeasesCount * renewalsPerLease;

        var store = new HighThroughputSeatStore(activeLeasesCount);
        var tokenIssuer = new FastLeaseTokenIssuer();
        var audit = new InMemoryAuditLedger();
        var engine = new LeaseEngine(store, tokenIssuer, audit, TimeProvider.System, NullLogger<LeaseEngine>.Instance);

        // Pre-allocate 100 seats
        var leases = new List<(string LeaseId, string Fp)>();
        for (int i = 0; i < activeLeasesCount; i++)
        {
            string fp = $"sha256:hb_holder_{i}";
            var acq = await engine.CheckoutAsync(new CheckoutCommand(
                LicenseId: "lic_massive_10k",
                Fingerprint: fp,
                MachineId: $"HB-NODE-{i}",
                Quantity: 1,
                Features: null,
                IdempotencyKey: null,
                AllowQueue: false,
                Ttl: TimeSpan.FromMinutes(15)));

            acq.IsSuccess.Should().BeTrue();
            leases.Add((acq.Allocations![0].LeaseId!, fp));
        }

        var heartbeatsWatch = Stopwatch.StartNew();
        var successfulRenewals = 0;

        await Parallel.ForEachAsync(
            leases,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount * 2 },
            async (leaseItem, ct) =>
            {
                for (int seq = 0; seq < renewalsPerLease; seq++)
                {
                    var renewRes = await engine.RenewAsync(
                        leaseId: leaseItem.LeaseId,
                        fingerprint: leaseItem.Fp,
                        clientSeq: seq,
                        ttl: TimeSpan.FromMinutes(15),
                        resurrectionWindow: TimeSpan.FromMinutes(5),
                        ct: ct);

                    if (renewRes.IsSuccess)
                    {
                        Interlocked.Increment(ref successfulRenewals);
                    }
                }
            });

        heartbeatsWatch.Stop();
        double throughputHb = totalHeartbeats / heartbeatsWatch.Elapsed.TotalSeconds;

        _output.WriteLine($"[10,000 HEARTBEAT BENCHMARK REPORT]");
        _output.WriteLine($"Total Heartbeats:     {totalHeartbeats:N0}");
        _output.WriteLine($"Successful Renewals:  {successfulRenewals:N0}");
        _output.WriteLine($"Elapsed Time:         {heartbeatsWatch.Elapsed.TotalMilliseconds:F1} ms");
        _output.WriteLine($"Heartbeat Throughput: {throughputHb:F0} renewals/sec");

        successfulRenewals.Should().Be(totalHeartbeats, "Every sequential heartbeat must succeed without deadlocks or sequence collisions");
        throughputHb.Should().BeGreaterThan(5000.0, "Heartbeat renewal throughput must exceed 5,000 renewals/sec");
    }

    [Fact]
    public void MassiveBenchmark_PostQuantum_VerificationThroughput_HighRate()
    {
        using var classicalProvider = Es256SignatureProvider.GenerateKey("bench-es256");
        byte[] payload = "{\"sub\":\"achilles_perf_10k\",\"aud\":\"cad_cluster\",\"exp\":1893456000,\"scope\":\"pqc\"}"u8.ToArray();

        byte[] esSignature = new byte[classicalProvider.SignatureSize];
        classicalProvider.Sign(payload, esSignature);

        // Benchmark Classical ECDSA ES256 verification (5,000 iterations)
        const int iterations = 5000;
        var swClassical = Stopwatch.StartNew();
        for (int i = 0; i < iterations; i++)
        {
            bool valid = classicalProvider.Verify(payload, esSignature);
            valid.Should().BeTrue();
        }
        swClassical.Stop();
        double esOpsSec = iterations / swClassical.Elapsed.TotalSeconds;

        _output.WriteLine($"[CRYPTOGRAPHIC VERIFICATION BENCHMARK]");
        _output.WriteLine($"ES256 Verifications:   {iterations:N0} in {swClassical.Elapsed.TotalMilliseconds:F1} ms ({esOpsSec:F0} ops/sec)");

        esOpsSec.Should().BeGreaterThan(2000.0, "ES256 verification throughput must exceed 2,000 ops/sec");

        // Benchmark Post-Quantum ML-DSA-65 if supported on host runtime
        if (MlDsaSignatureProvider.IsSupported)
        {
            using var pqcProvider = MlDsaSignatureProvider.GenerateKey(System.Security.Cryptography.MLDsaAlgorithm.MLDsa65, "bench-mldsa65");
            byte[] pqcSignature = new byte[pqcProvider.SignatureSize];
            pqcProvider.Sign(payload, pqcSignature);

            const int pqcIterations = 1000;
            var swPqc = Stopwatch.StartNew();
            for (int i = 0; i < pqcIterations; i++)
            {
                bool valid = pqcProvider.Verify(payload, pqcSignature);
                valid.Should().BeTrue();
            }
            swPqc.Stop();
            double pqcOpsSec = pqcIterations / swPqc.Elapsed.TotalSeconds;

            _output.WriteLine($"ML-DSA-65 Verifications: {pqcIterations:N0} in {swPqc.Elapsed.TotalMilliseconds:F1} ms ({pqcOpsSec:F0} ops/sec)");
            pqcOpsSec.Should().BeGreaterThan(200.0, "ML-DSA-65 verification throughput must exceed 200 ops/sec");
        }
    }
}
