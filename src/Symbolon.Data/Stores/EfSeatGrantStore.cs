using Microsoft.EntityFrameworkCore;
using Symbolon.Data.Entities;
using Symbolon.Domain.Grants;

namespace Symbolon.Data.Stores;

public sealed class EfSeatGrantStore(SymbolonDbContext db) : ISeatGrantStore
{
    public async Task<IReadOnlyList<SeatGrantRecord>> GetActiveGrantsForLicenseAsync(
        string licenseId,
        DateTimeOffset now,
        CancellationToken ct = default)
    {
        var entities = await db.SeatGrants
            .AsNoTracking()
            .Where(g => g.LicenseId == licenseId &&
                        g.RevokedAt == null &&
                        g.NotBefore <= now &&
                        g.NotAfter >= now)
            .OrderBy(g => g.SeatFrom)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return entities.Select(ToRecord).ToList();
    }

    public async Task<IReadOnlyList<SeatGrantRecord>> GetGrantsForLicenseAsync(
        string licenseId,
        CancellationToken ct = default)
    {
        var entities = await db.SeatGrants
            .AsNoTracking()
            .Where(g => g.LicenseId == licenseId)
            .OrderByDescending(g => g.Seq)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return entities.Select(ToRecord).ToList();
    }

    public async Task<IReadOnlyList<SeatGrantRecord>> GetAllGrantsAsync(
        CancellationToken ct = default)
    {
        var entities = await db.SeatGrants
            .AsNoTracking()
            .OrderByDescending(g => g.NotBefore)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return entities.Select(ToRecord).ToList();
    }

    public async Task<SeatGrantRecord?> GetGrantByIdAsync(
        string grantId,
        CancellationToken ct = default)
    {
        var entity = await db.SeatGrants
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == grantId, ct)
            .ConfigureAwait(false);

        return entity is null ? null : ToRecord(entity);
    }

    public async Task<long> GetHighestSeqAsync(
        string licenseId,
        string relayId,
        CancellationToken ct = default)
    {
        var maxSeq = await db.SeatGrants
            .Where(g => g.LicenseId == licenseId && g.RelayId == relayId)
            .Select(g => (long?)g.Seq)
            .MaxAsync(ct)
            .ConfigureAwait(false);

        return maxSeq ?? 0L;
    }

    public async Task<bool> HasNonceBeenSeenAsync(
        string nonce,
        CancellationToken ct = default)
    {
        return await db.AirGapNonces
            .AnyAsync(n => n.Nonce == nonce, ct)
            .ConfigureAwait(false);
    }

    public async Task RecordNonceAsync(
        string nonce,
        string relayId,
        DateTimeOffset seenAt,
        DateTimeOffset expiresAt,
        CancellationToken ct = default)
    {
        var entity = new AirGapNonceEntity
        {
            Nonce = nonce,
            RelayId = relayId,
            SeenAt = seenAt,
            ExpiresAt = expiresAt
        };

        db.AirGapNonces.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
    }

    public async Task<SeatGrantRecord> SaveGrantAsync(
        SeatGrantRecord grant,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(grant);

        // GNT-9: If grant supersedes an older seq, revoke superseded grants
        if (grant.Supersedes.HasValue)
        {
            var superseded = await db.SeatGrants
                .Where(g => g.LicenseId == grant.LicenseId &&
                            g.RelayId == grant.RelayId &&
                            g.Seq <= grant.Supersedes.Value &&
                            g.RevokedAt == null)
                .ToListAsync(ct)
                .ConfigureAwait(false);

            foreach (var old in superseded)
            {
                old.RevokedAt = grant.NotBefore;
            }
        }

        var entity = new SeatGrantEntity
        {
            Id = grant.Id,
            LicenseId = grant.LicenseId,
            RelayId = grant.RelayId,
            Seats = grant.Seats,
            SeatFrom = grant.SeatFrom,
            SeatTo = grant.SeatTo,
            Seq = grant.Seq,
            Supersedes = grant.Supersedes,
            NotBefore = grant.NotBefore,
            NotAfter = grant.NotAfter,
            RevokedAt = grant.RevokedAt,
            Document = grant.Document
        };

        db.SeatGrants.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return ToRecord(entity);
    }

    public async Task<bool> RevokeGrantAsync(
        string grantId,
        DateTimeOffset revokedAt,
        CancellationToken ct = default)
    {
        var entity = await db.SeatGrants
            .FirstOrDefaultAsync(g => g.Id == grantId, ct)
            .ConfigureAwait(false);

        if (entity is null || entity.RevokedAt.HasValue)
        {
            return false;
        }

        entity.RevokedAt = revokedAt;
        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return true;
    }

    private static SeatGrantRecord ToRecord(SeatGrantEntity e) =>
        new(
            Id: e.Id,
            LicenseId: e.LicenseId,
            RelayId: e.RelayId,
            Seats: e.Seats,
            SeatFrom: e.SeatFrom,
            SeatTo: e.SeatTo,
            Seq: e.Seq,
            Supersedes: e.Supersedes,
            NotBefore: e.NotBefore,
            NotAfter: e.NotAfter,
            RevokedAt: e.RevokedAt,
            Document: e.Document);
}
