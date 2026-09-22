using Microsoft.EntityFrameworkCore;
using Symbolon.Data.Entities;
using Symbolon.Domain.Tokens;

namespace Symbolon.Data.Stores;

public sealed class EfTokenStore(SymbolonDbContext db) : ITokenStore
{
    public async Task<TokenWalletModel> CreateWalletAsync(TokenWalletModel wallet, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(wallet);

        var entity = new TokenWalletEntity
        {
            Id = wallet.Id,
            TenantId = wallet.TenantId,
            LicenseId = wallet.LicenseId,
            Code = wallet.Code,
            Name = wallet.Name,
            TotalCredits = wallet.TotalCredits,
            Balance = wallet.Balance,
            ReservedCredits = wallet.ReservedCredits,
            OverdraftLimit = wallet.OverdraftLimit,
            State = wallet.State,
            ExpiresAt = wallet.ExpiresAt,
            ThresholdLowAlert = wallet.ThresholdLowAlert,
            CreatedAt = wallet.CreatedAt,
            LastRefillAt = wallet.LastRefillAt
        };

        db.TokenWallets.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return ToModel(entity);
    }

    public async Task<TokenWalletModel?> GetWalletByIdAsync(string walletId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(walletId);

        var entity = await db.TokenWallets
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.Id == walletId, ct)
            .ConfigureAwait(false);

        return entity is null ? null : ToModel(entity);
    }

    public async Task<TokenWalletModel?> GetWalletByCodeAsync(string tenantId, string code, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(code);

        string normCode = code.Trim().ToUpperInvariant();

        var entity = await db.TokenWallets
            .AsNoTracking()
            .FirstOrDefaultAsync(w => w.TenantId == tenantId && w.Code == normCode, ct)
            .ConfigureAwait(false);

        return entity is null ? null : ToModel(entity);
    }

    public async Task<IReadOnlyList<TokenWalletModel>> ListWalletsAsync(string? tenantId, CancellationToken ct = default)
    {
        var query = db.TokenWallets.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            query = query.Where(w => w.TenantId == tenantId);
        }

        var entities = await query
            .OrderByDescending(w => w.CreatedAt)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return entities.Select(ToModel).ToList();
    }

    public async Task<TokenWalletModel> UpdateWalletAsync(TokenWalletModel wallet, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(wallet);

        var entity = await db.TokenWallets.FirstOrDefaultAsync(w => w.Id == wallet.Id, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Token wallet '{wallet.Id}' not found.");

        entity.Name = wallet.Name;
        entity.State = wallet.State;
        entity.OverdraftLimit = wallet.OverdraftLimit;
        entity.ThresholdLowAlert = wallet.ThresholdLowAlert;
        entity.ExpiresAt = wallet.ExpiresAt;

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToModel(entity);
    }

    public async Task<TokenRateModel> SetRateAsync(TokenRateModel rate, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(rate);

        var entity = await db.TokenRates
            .FirstOrDefaultAsync(r => r.TenantId == rate.TenantId && r.ProductId == rate.ProductId && r.FeatureCode == rate.FeatureCode, ct)
            .ConfigureAwait(false);

        if (entity is null)
        {
            entity = new TokenRateEntity
            {
                Id = rate.Id,
                TenantId = rate.TenantId,
                ProductId = rate.ProductId,
                FeatureCode = rate.FeatureCode,
                RatePerMinute = rate.RatePerMinute,
                RatePerUnit = rate.RatePerUnit,
                Description = rate.Description,
                CreatedAt = rate.CreatedAt,
                UpdatedAt = rate.UpdatedAt
            };
            db.TokenRates.Add(entity);
        }
        else
        {
            entity.RatePerMinute = rate.RatePerMinute;
            entity.RatePerUnit = rate.RatePerUnit;
            entity.Description = rate.Description;
            entity.UpdatedAt = rate.UpdatedAt;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToModel(entity);
    }

    public async Task<TokenRateModel?> GetRateAsync(string tenantId, string? productId, string featureCode, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(tenantId);
        ArgumentNullException.ThrowIfNull(featureCode);

        string normCode = featureCode.Trim().ToUpperInvariant();

        var query = db.TokenRates.AsNoTracking().Where(r => r.TenantId == tenantId && r.FeatureCode == normCode);

        if (!string.IsNullOrWhiteSpace(productId))
        {
            query = query.Where(r => r.ProductId == productId || r.ProductId == null);
        }

        var entity = await query.OrderByDescending(r => r.ProductId != null).FirstOrDefaultAsync(ct).ConfigureAwait(false);
        return entity is null ? null : ToModel(entity);
    }

    public async Task<IReadOnlyList<TokenRateModel>> ListRatesAsync(string? tenantId, string? productId, CancellationToken ct = default)
    {
        var query = db.TokenRates.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(tenantId))
        {
            query = query.Where(r => r.TenantId == tenantId);
        }

        if (!string.IsNullOrWhiteSpace(productId))
        {
            query = query.Where(r => r.ProductId == productId);
        }

        var entities = await query.OrderBy(r => r.FeatureCode).ToListAsync(ct).ConfigureAwait(false);
        return entities.Select(ToModel).ToList();
    }

    public async Task<TokenReservationModel?> GetReservationByIdAsync(string reservationId, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reservationId);

        var entity = await db.TokenReservations
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.Id == reservationId, ct)
            .ConfigureAwait(false);

        return entity is null ? null : ToModel(entity);
    }

    public async Task<TokenReservationModel?> FindReservationByIdempotencyAsync(string idempotencyKey, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(idempotencyKey);

        var entity = await db.TokenReservations
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.IdempotencyKey == idempotencyKey, ct)
            .ConfigureAwait(false);

        return entity is null ? null : ToModel(entity);
    }

    public async Task<TokenReservationModel> SaveReservationAsync(TokenReservationModel reservation, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(reservation);

        var entity = await db.TokenReservations.FirstOrDefaultAsync(r => r.Id == reservation.Id, ct).ConfigureAwait(false);

        if (entity is null)
        {
            entity = new TokenReservationEntity
            {
                Id = reservation.Id,
                WalletId = reservation.WalletId,
                FeatureCode = reservation.FeatureCode,
                ReservedAmount = reservation.ReservedAmount,
                ConsumedAmount = reservation.ConsumedAmount,
                MachineId = reservation.MachineId,
                ClientRef = reservation.ClientRef,
                Status = reservation.Status,
                IdempotencyKey = reservation.IdempotencyKey,
                ExpiresAt = reservation.ExpiresAt,
                CreatedAt = reservation.CreatedAt,
                UpdatedAt = reservation.UpdatedAt
            };
            db.TokenReservations.Add(entity);
        }
        else
        {
            entity.ConsumedAmount = reservation.ConsumedAmount;
            entity.Status = reservation.Status;
            entity.ExpiresAt = reservation.ExpiresAt;
            entity.UpdatedAt = reservation.UpdatedAt;
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToModel(entity);
    }

    public async Task<TokenLedgerEntryModel> AddLedgerEntryAsync(TokenLedgerEntryModel entry, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(entry);

        var entity = new TokenLedgerEntryEntity
        {
            WalletId = entry.WalletId,
            ReservationId = entry.ReservationId,
            TransactionType = entry.TransactionType,
            Amount = entry.Amount,
            BalanceAfter = entry.BalanceAfter,
            FeatureCode = entry.FeatureCode,
            IdempotencyKey = entry.IdempotencyKey,
            Timestamp = entry.Timestamp,
            MetadataJson = entry.MetadataJson
        };

        db.TokenLedgerEntries.Add(entity);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return ToModel(entity);
    }

    public async Task<IReadOnlyList<TokenLedgerEntryModel>> ListLedgerEntriesAsync(string? walletId, int limit, CancellationToken ct = default)
    {
        var query = db.TokenLedgerEntries.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(walletId))
        {
            query = query.Where(e => e.WalletId == walletId);
        }

        int takeCount = limit > 0 ? Math.Min(limit, 200) : 50;

        var entities = await query
            .OrderByDescending(e => e.Timestamp)
            .Take(takeCount)
            .ToListAsync(ct)
            .ConfigureAwait(false);

        return entities.Select(ToModel).ToList();
    }

    public async Task<TokenWalletModel> UpdateWalletBalanceAtomicAsync(
        string walletId,
        decimal balanceDelta,
        decimal reservedDelta,
        decimal totalCreditsDelta,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(walletId);

        var entity = await db.TokenWallets.FirstOrDefaultAsync(w => w.Id == walletId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Token wallet '{walletId}' not found.");

        entity.Balance += balanceDelta;
        entity.ReservedCredits += reservedDelta;
        entity.TotalCredits += totalCreditsDelta;

        if (totalCreditsDelta > 0)
        {
            entity.LastRefillAt = DateTimeOffset.UtcNow;
        }

        if (entity.Balance <= 0 && (entity.Balance + entity.OverdraftLimit) <= 0)
        {
            entity.State = "depleted";
        }
        else if (entity.State == "depleted" && entity.Balance > 0)
        {
            entity.State = "active";
        }

        await db.SaveChangesAsync(ct).ConfigureAwait(false);
        return ToModel(entity);
    }

    private static TokenWalletModel ToModel(TokenWalletEntity e) =>
        new(e.Id, e.TenantId, e.LicenseId, e.Code, e.Name, e.TotalCredits, e.Balance, e.ReservedCredits, e.OverdraftLimit, e.State, e.ExpiresAt, e.ThresholdLowAlert, e.CreatedAt, e.LastRefillAt);

    private static TokenRateModel ToModel(TokenRateEntity e) =>
        new(e.Id, e.TenantId, e.ProductId, e.FeatureCode, e.RatePerMinute, e.RatePerUnit, e.Description, e.CreatedAt, e.UpdatedAt);

    private static TokenReservationModel ToModel(TokenReservationEntity e) =>
        new(e.Id, e.WalletId, e.FeatureCode, e.ReservedAmount, e.ConsumedAmount, e.MachineId, e.ClientRef, e.Status, e.IdempotencyKey, e.ExpiresAt, e.CreatedAt, e.UpdatedAt);

    private static TokenLedgerEntryModel ToModel(TokenLedgerEntryEntity e) =>
        new(e.Id, e.WalletId, e.ReservationId, e.TransactionType, e.Amount, e.BalanceAfter, e.FeatureCode, e.IdempotencyKey, e.Timestamp, e.MetadataJson);
}
