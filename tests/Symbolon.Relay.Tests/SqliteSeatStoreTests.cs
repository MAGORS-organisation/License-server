using FluentAssertions;
using Xunit;

namespace Symbolon.Relay.Tests;

public sealed class SqliteSeatStoreTests : IDisposable
{
    private readonly string _dbPath;
    private readonly SqliteSeatStore _store;

    public SqliteSeatStoreTests()
    {
        _dbPath = $"test_{Guid.NewGuid():N}.db";
        _store = new SqliteSeatStore($"Data Source={_dbPath}");
    }

    [Fact]
    public async Task SeedAndAcquireSeats_ExhaustsAtLimit()
    {
        string licId = "lic_sqlite_test";
        await _store.SeedSeatsAsync(licId, 2);

        var now = DateTimeOffset.UtcNow;
        var ttl = TimeSpan.FromMinutes(10);

        var seat1 = await _store.TryAcquireOneAsync(licId, "sha256:fp1", "pc1", now, ttl);
        seat1.Should().NotBeNull();
        seat1!.SeatNo.Should().Be(0);

        var seat2 = await _store.TryAcquireOneAsync(licId, "sha256:fp2", "pc2", now, ttl);
        seat2.Should().NotBeNull();
        seat2!.SeatNo.Should().Be(1);

        // 3rd request should fail
        var seat3 = await _store.TryAcquireOneAsync(licId, "sha256:fp3", "pc3", now, ttl);
        seat3.Should().BeNull();
    }

    [Fact]
    public async Task RenewAsync_UpdatesExpiryAndMonotonicSequence()
    {
        string licId = "lic_renew_test";
        await _store.SeedSeatsAsync(licId, 1);

        var now = DateTimeOffset.UtcNow;
        var ttl = TimeSpan.FromMinutes(10);
        var rWindow = TimeSpan.FromMinutes(5);

        var seat = await _store.TryAcquireOneAsync(licId, "sha256:fp1", "pc1", now, ttl);
        string leaseId = seat!.LeaseId!;

        // First renew
        var outcome1 = await _store.TryRenewAsync(leaseId, "sha256:fp1", 0, now, ttl, rWindow);
        outcome1.Type.Should().Be(Domain.RenewOutcomeType.Renewed);
        outcome1.Allocation!.LeaseSeq.Should().Be(1);

        // Stale sequence renew
        var outcome2 = await _store.TryRenewAsync(leaseId, "sha256:fp1", 0, now, ttl, rWindow);
        outcome2.Type.Should().Be(Domain.RenewOutcomeType.SeqReplay);

        // Renew with wrong holder
        var outcome3 = await _store.TryRenewAsync(leaseId, "sha256:wrong_fp", 1, now, ttl, rWindow);
        outcome3.Type.Should().Be(Domain.RenewOutcomeType.Conflict);
    }

    [Fact]
    public async Task ReleaseAsync_FreesSeatImmediately()
    {
        string licId = "lic_release_test";
        await _store.SeedSeatsAsync(licId, 1);

        var now = DateTimeOffset.UtcNow;
        var ttl = TimeSpan.FromMinutes(10);

        var seat = await _store.TryAcquireOneAsync(licId, "sha256:fp1", "pc1", now, ttl);
        string leaseId = seat!.LeaseId!;

        bool released = await _store.TryReleaseAsync(leaseId, now);
        released.Should().BeTrue();

        // New acquire succeeds
        var seat2 = await _store.TryAcquireOneAsync(licId, "sha256:fp2", "pc2", now, ttl);
        seat2.Should().NotBeNull();
        seat2!.SeatNo.Should().Be(0);
    }

    [Fact]
    public async Task TryAcquireOne_UnderHighConcurrency_GuaranteesZeroDoubleAllocation()
    {
        string licId = "lic_sqlite_concurrency";
        const int totalSeats = 5;
        const int concurrentClients = 50;

        await _store.SeedSeatsAsync(licId, totalSeats);

        var now = DateTimeOffset.UtcNow;
        var ttl = TimeSpan.FromMinutes(15);

        using var barrier = new SemaphoreSlim(0, concurrentClients);
        var tasks = new Task<Domain.SeatAllocation?>[concurrentClients];

        for (int i = 0; i < concurrentClients; i++)
        {
            int clientId = i;
            tasks[i] = Task.Run(async () =>
            {
                barrier.Release();
                await Task.Yield();

                return await _store.TryAcquireOneAsync(
                    licId,
                    $"sha256:fp_{clientId}",
                    $"pc_{clientId}",
                    now,
                    ttl);
            });
        }

        var results = await Task.WhenAll(tasks);
        var acquired = results.Where(s => s is not null).ToList();

        // Exactly totalSeats allocations must succeed
        acquired.Should().HaveCount(totalSeats);

        // All allocated seat numbers must be distinct
        var seatNumbers = acquired.Select(s => s!.SeatNo).Distinct().ToList();
        seatNumbers.Should().HaveCount(totalSeats);
        seatNumbers.Should().BeEquivalentTo(Enumerable.Range(0, totalSeats));
    }

    public void Dispose()
    {
        _store.Dispose();
        if (File.Exists(_dbPath))
        {
            try
            {
                File.Delete(_dbPath);
            }
            catch (IOException)
            {
                // ignore in-use temp file on test completion
            }
            catch (UnauthorizedAccessException)
            {
                // ignore
            }
        }
    }
}
