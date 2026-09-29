using System.Collections.Concurrent;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Symbolon.Data.Entities;
using Symbolon.Domain;

namespace Symbolon.Data.Stores;

public sealed class EfSeatStore(SymbolonDbContext db) : ISeatStore
{
    private static readonly ConcurrentDictionary<string, (DateTimeOffset ExpiresAt, SeatAllocation[] Allocations)> IdempotencyCache = new();

    public async Task<SeatAllocation?> TryAcquireOneAsync(
        string licenseId,
        string fingerprint,
        string? machineId,
        DateTimeOffset now,
        TimeSpan ttl,
        string? reservationTarget = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(licenseId);
        ArgumentNullException.ThrowIfNull(fingerprint);

        const int maxRetries = 3;
        for (int retry = 0; retry < maxRetries; retry++)
        {
            var seats = await db.Seats
                .Where(s => s.LicenseId == licenseId && s.GrantId == null)
                .OrderBy(s => s.IsOverage)
                .ThenBy(s => s.SeatNo)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            var freeSeats = seats.Where(s =>
                (s.LeaseId == null || s.ExpiresAt < now) &&
                (s.BorrowedUntil == null || s.BorrowedUntil < now)).ToList();

            SeatEntity? candidate = null;
            if (!string.IsNullOrWhiteSpace(reservationTarget))
            {
                candidate = freeSeats.FirstOrDefault(s => string.Equals(s.ReservedFor, reservationTarget, StringComparison.OrdinalIgnoreCase));
            }

            candidate ??= freeSeats.FirstOrDefault(s => s.ReservedFor == null);

            if (candidate is null)
            {
                return null;
            }

            string leaseId = $"lse_{Guid.NewGuid():N}";
            byte[] fpBytes = Encoding.UTF8.GetBytes(fingerprint);
            var expiresAt = now + ttl;

            candidate.LeaseId = leaseId;
            candidate.HolderFp = fpBytes;
            candidate.MachineId = machineId;
            candidate.AcquiredAt = now;
            candidate.ExpiresAt = expiresAt;
            candidate.LeaseSeq = 0;

            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);

                return new SeatAllocation
                {
                    SeatId = candidate.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    SeatNo = candidate.SeatNo,
                    LicenseId = candidate.LicenseId,
                    LeaseId = leaseId,
                    HolderFingerprint = fingerprint,
                    MachineId = machineId,
                    AcquiredAt = now,
                    ExpiresAt = expiresAt,
                    LeaseSeq = 0,
                    IsOverage = candidate.IsOverage
                };
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();
            }
        }

        return null;
    }

    public async Task<SeatAllocation[]?> TryAcquireManyAsync(
        string licenseId,
        string fingerprint,
        string? machineId,
        int quantity,
        DateTimeOffset now,
        TimeSpan ttl,
        string? reservationTarget = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(licenseId);
        ArgumentNullException.ThrowIfNull(fingerprint);

        if (quantity <= 0)
        {
            return null;
        }

        const int maxRetries = 3;
        for (int retry = 0; retry < maxRetries; retry++)
        {
            var seats = await db.Seats
                .Where(s => s.LicenseId == licenseId && s.GrantId == null)
                .OrderBy(s => s.IsOverage)
                .ThenBy(s => s.SeatNo)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            var freeSeats = seats
                .Where(s => (s.LeaseId == null || s.ExpiresAt < now) &&
                            (s.BorrowedUntil == null || s.BorrowedUntil < now))
                .ToList();

            var matchingReserved = !string.IsNullOrWhiteSpace(reservationTarget)
                ? freeSeats.Where(s => string.Equals(s.ReservedFor, reservationTarget, StringComparison.OrdinalIgnoreCase))
                : Enumerable.Empty<SeatEntity>();

            var unreserved = freeSeats.Where(s => s.ReservedFor == null);

            var candidates = matchingReserved.Concat(unreserved).Take(quantity).ToList();

            if (candidates.Count < quantity)
            {
                return null;
            }

            string leaseId = $"lse_{Guid.NewGuid():N}";
            byte[] fpBytes = Encoding.UTF8.GetBytes(fingerprint);
            var expiresAt = now + ttl;
            var allocations = new SeatAllocation[quantity];

            for (int i = 0; i < quantity; i++)
            {
                var seat = candidates[i];
                seat.LeaseId = leaseId;
                seat.HolderFp = fpBytes;
                seat.MachineId = machineId;
                seat.AcquiredAt = now;
                seat.ExpiresAt = expiresAt;
                seat.LeaseSeq = 0;

                allocations[i] = new SeatAllocation
                {
                    SeatId = seat.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    SeatNo = seat.SeatNo,
                    LicenseId = seat.LicenseId,
                    LeaseId = leaseId,
                    HolderFingerprint = fingerprint,
                    MachineId = machineId,
                    AcquiredAt = now,
                    ExpiresAt = expiresAt,
                    LeaseSeq = 0,
                    IsOverage = seat.IsOverage
                };
            }

            try
            {
                await db.SaveChangesAsync(ct).ConfigureAwait(false);
                return allocations;
            }
            catch (DbUpdateConcurrencyException)
            {
                db.ChangeTracker.Clear();
            }
        }

        return null;
    }

    public async Task<RenewOutcome> TryRenewAsync(
        string leaseId,
        string fingerprint,
        long clientSeq,
        DateTimeOffset now,
        TimeSpan ttl,
        TimeSpan resurrectionWindow,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(leaseId);
        ArgumentNullException.ThrowIfNull(fingerprint);

        var seat = await db.Seats
            .FirstOrDefaultAsync(s => s.LeaseId == leaseId, ct)
            .ConfigureAwait(false);

        if (seat is null)
        {
            return RenewOutcome.Unknown;
        }

        byte[] fpBytes = Encoding.UTF8.GetBytes(fingerprint);
        if (seat.HolderFp is null || !seat.HolderFp.AsSpan().SequenceEqual(fpBytes))
        {
            return RenewOutcome.Taken;
        }

        if (seat.LeaseSeq != clientSeq)
        {
            return RenewOutcome.SeqReplay;
        }

        if (seat.ExpiresAt.HasValue && seat.ExpiresAt.Value < now - resurrectionWindow)
        {
            return RenewOutcome.Taken;
        }

        bool isResurrected = seat.ExpiresAt.HasValue && seat.ExpiresAt.Value < now;
        var newExpiresAt = now + ttl;
        seat.ExpiresAt = newExpiresAt;
        seat.LeaseSeq++;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var allocation = new SeatAllocation
        {
            SeatId = seat.Id.ToString(System.Globalization.CultureInfo.InvariantCulture),
            SeatNo = seat.SeatNo,
            LicenseId = seat.LicenseId,
            LeaseId = leaseId,
            HolderFingerprint = fingerprint,
            MachineId = seat.MachineId,
            AcquiredAt = seat.AcquiredAt,
            ExpiresAt = newExpiresAt,
            LeaseSeq = seat.LeaseSeq,
            IsOverage = seat.IsOverage
        };
        return isResurrected ? RenewOutcome.Resurrected(allocation) : RenewOutcome.Renewed(allocation);
    }

    public async Task<bool> TryReleaseAsync(string leaseId, DateTimeOffset now, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(leaseId);

        var seats = await db.Seats
            .Where(s => s.LeaseId == leaseId)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        if (seats.Count == 0)
        {
            return false;
        }

        foreach (var seat in seats)
        {
            seat.LeaseId = null;
            seat.HolderFp = null;
            seat.MachineId = null;
            seat.AcquiredAt = null;
            seat.ExpiresAt = null;
            seat.BorrowedUntil = null;
            seat.UserId = null;
            seat.LeaseSeq = 0;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    public Task<SeatAllocation[]?> TryGetIdempotentAsync(
        string licenseId,
        string idempotencyKey,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        string key = $"{licenseId}:{idempotencyKey}";
        if (IdempotencyCache.TryGetValue(key, out var cached))
        {
            if (cached.ExpiresAt > now)
            {
                return Task.FromResult<SeatAllocation[]?>(cached.Allocations);
            }
            IdempotencyCache.TryRemove(key, out _);
        }

        return Task.FromResult<SeatAllocation[]?>(null);
    }

    public Task SaveIdempotentAsync(
        string licenseId,
        string idempotencyKey,
        SeatAllocation[] allocations,
        DateTimeOffset now,
        TimeSpan ttl,
        CancellationToken ct = default)
    {
        string key = $"{licenseId}:{idempotencyKey}";
        IdempotencyCache[key] = (now + ttl, allocations);

        if (IdempotencyCache.Count > 500)
        {
            foreach (var kv in IdempotencyCache)
            {
                if (kv.Value.ExpiresAt <= now)
                {
                    IdempotencyCache.TryRemove(kv.Key, out _);
                }
            }
        }

        return Task.CompletedTask;
    }

    public async Task<TimeSpan?> EstimateWaitAsync(string licenseId, DateTimeOffset now, CancellationToken ct = default)
    {
        var activeSeats = await db.Seats
            .Where(s => s.LicenseId == licenseId && s.LeaseId != null)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var minExpiry = activeSeats
            .Where(s => s.ExpiresAt.HasValue && s.ExpiresAt.Value > now)
            .Select(s => s.ExpiresAt)
            .DefaultIfEmpty(null)
            .Min();

        if (minExpiry.HasValue)
        {
            var wait = minExpiry.Value - now;
            return wait > TimeSpan.Zero ? wait : TimeSpan.FromSeconds(5);
        }

        return null;
    }

    public async Task SyncSeatReservationsAsync(
        string licenseId,
        IReadOnlyList<(string Target, int Count)> reservations,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(licenseId);
        ArgumentNullException.ThrowIfNull(reservations);

        var now = DateTimeOffset.UtcNow;
        var seats = await db.Seats
            .Where(s => s.LicenseId == licenseId && s.GrantId == null)
            .OrderBy(s => s.IsOverage)
            .ThenBy(s => s.SeatNo)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        var desiredMap = reservations.ToDictionary(r => r.Target, r => r.Count, StringComparer.OrdinalIgnoreCase);

        // 1. For targets that are no longer in desired reservations, clear ReservedFor on free seats (FLT-25)
        foreach (var seat in seats)
        {
            if (seat.ReservedFor != null && !desiredMap.ContainsKey(seat.ReservedFor))
            {
                bool isFree = (seat.LeaseId == null || seat.ExpiresAt < now) && (seat.BorrowedUntil == null || seat.BorrowedUntil < now);
                if (isFree)
                {
                    seat.ReservedFor = null;
                }
            }
        }

        // 2. Adjust counts for desired reservations (FLT-23)
        foreach (var (target, count) in reservations)
        {
            int currentReservedCount = seats.Count(s => string.Equals(s.ReservedFor, target, StringComparison.OrdinalIgnoreCase));

            if (currentReservedCount < count)
            {
                int needMore = count - currentReservedCount;
                var unreservedFreeSeats = seats
                    .Where(s => s.ReservedFor == null &&
                                (s.LeaseId == null || s.ExpiresAt < now) &&
                                (s.BorrowedUntil == null || s.BorrowedUntil < now))
                    .Take(needMore)
                    .ToList();

                foreach (var s in unreservedFreeSeats)
                {
                    s.ReservedFor = target;
                }
            }
            else if (currentReservedCount > count)
            {
                int reduceBy = currentReservedCount - count;
                var freeToRemove = seats
                    .Where(s => string.Equals(s.ReservedFor, target, StringComparison.OrdinalIgnoreCase) &&
                                (s.LeaseId == null || s.ExpiresAt < now) &&
                                (s.BorrowedUntil == null || s.BorrowedUntil < now))
                    .Take(reduceBy)
                    .ToList();

                foreach (var s in freeToRemove)
                {
                    s.ReservedFor = null;
                }
            }
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
