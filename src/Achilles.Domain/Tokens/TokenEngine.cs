using System.Globalization;

namespace Achilles.Domain.Tokens;

public sealed class TokenEngine : ITokenEngine
{
    private readonly ITokenStore _store;
    private readonly TimeProvider _timeProvider;

    public TokenEngine(ITokenStore store, TimeProvider? timeProvider = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task<TokenWalletModel> CreateWalletAsync(CreateWalletRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Code);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);

        var now = _timeProvider.GetUtcNow();
        var walletId = $"wal_{Guid.NewGuid():N}";

        var wallet = new TokenWalletModel(
            Id: walletId,
            TenantId: request.TenantId,
            LicenseId: request.LicenseId,
            Code: request.Code.Trim().ToUpperInvariant(),
            Name: request.Name.Trim(),
            TotalCredits: Math.Max(0, request.InitialCredits),
            Balance: Math.Max(0, request.InitialCredits),
            ReservedCredits: 0,
            OverdraftLimit: Math.Max(0, request.OverdraftLimit),
            State: "active",
            ExpiresAt: request.ExpiresAt,
            ThresholdLowAlert: request.ThresholdLowAlert,
            CreatedAt: now,
            LastRefillAt: request.InitialCredits > 0 ? now : null);

        var created = await _store.CreateWalletAsync(wallet, ct).ConfigureAwait(false);

        if (request.InitialCredits > 0)
        {
            await _store.AddLedgerEntryAsync(new TokenLedgerEntryModel(
                Id: 0,
                WalletId: created.Id,
                ReservationId: null,
                TransactionType: "credit",
                Amount: request.InitialCredits,
                BalanceAfter: created.Balance,
                FeatureCode: null,
                IdempotencyKey: $"init_{walletId}",
                Timestamp: now,
                MetadataJson: "{\"reason\":\"Initial credit on wallet creation\"}"), ct).ConfigureAwait(false);
        }

        return created;
    }

    public async Task<TokenWalletModel> CreditWalletAsync(CreditWalletRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WalletId);

        if (request.Amount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "Credit amount must be greater than zero.");
        }

        var wallet = await _store.GetWalletByIdAsync(request.WalletId, ct).ConfigureAwait(false)
            ?? throw new KeyNotFoundException($"Token wallet '{request.WalletId}' not found.");

        var now = _timeProvider.GetUtcNow();

        var updated = await _store.UpdateWalletBalanceAtomicAsync(
            walletId: wallet.Id,
            balanceDelta: request.Amount,
            reservedDelta: 0,
            totalCreditsDelta: request.Amount,
            ct: ct).ConfigureAwait(false);

        await _store.AddLedgerEntryAsync(new TokenLedgerEntryModel(
            Id: 0,
            WalletId: updated.Id,
            ReservationId: null,
            TransactionType: "credit",
            Amount: request.Amount,
            BalanceAfter: updated.Balance,
            FeatureCode: null,
            IdempotencyKey: request.IdempotencyKey,
            Timestamp: now,
            MetadataJson: $"{{\"reason\":\"{request.Reason.Replace("\"", "\\\"", StringComparison.Ordinal)}\"}}"), ct).ConfigureAwait(false);

        return updated;
    }

    public async Task<ReserveTokensResult> ReserveTokensAsync(ReserveTokensRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.WalletId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FeatureCode);

        if (request.EstimatedUnits <= 0)
        {
            return new ReserveTokensResult(false, null, 0, 0, 0, "Estimated units must be greater than zero.");
        }

        // Idempotency check
        if (!string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            var existingRes = await _store.FindReservationByIdempotencyAsync(request.IdempotencyKey, ct).ConfigureAwait(false);
            if (existingRes is not null && existingRes.Status == "pending")
            {
                var w = await _store.GetWalletByIdAsync(existingRes.WalletId, ct).ConfigureAwait(false);
                decimal avail = w != null ? (w.Balance + w.OverdraftLimit) - w.ReservedCredits : 0;
                return new ReserveTokensResult(true, existingRes.Id, existingRes.ReservedAmount, avail, w?.OverdraftLimit ?? 0);
            }
        }

        var wallet = await _store.GetWalletByIdAsync(request.WalletId, ct).ConfigureAwait(false);
        if (wallet is null)
        {
            return new ReserveTokensResult(false, null, 0, 0, 0, $"Token wallet '{request.WalletId}' was not found.");
        }

        var now = _timeProvider.GetUtcNow();
        if (wallet.ExpiresAt.HasValue && wallet.ExpiresAt.Value < now)
        {
            return new ReserveTokensResult(false, null, 0, 0, 0, "Token wallet has expired.");
        }

        if (string.Equals(wallet.State, "frozen", StringComparison.OrdinalIgnoreCase))
        {
            return new ReserveTokensResult(false, null, 0, 0, 0, "Token wallet is frozen.");
        }

        // Calculate credits needed based on configured rate
        decimal creditsNeeded = await CalculateCreditsAsync(
            wallet.TenantId,
            request.FeatureCode,
            request.EstimatedUnits,
            request.IsDurationMinutes,
            ct).ConfigureAwait(false);

        decimal availableWithOverdraft = (wallet.Balance + wallet.OverdraftLimit) - wallet.ReservedCredits;

        if (availableWithOverdraft < creditsNeeded)
        {
            return new ReserveTokensResult(
                Success: false,
                ReservationId: null,
                ReservedAmount: 0,
                AvailableBalance: Math.Max(0, wallet.Balance - wallet.ReservedCredits),
                OverdraftRemaining: Math.Max(0, availableWithOverdraft),
                FailureReason: $"Insufficient credits. Needed {creditsNeeded.ToString(CultureInfo.InvariantCulture)}, available {availableWithOverdraft.ToString(CultureInfo.InvariantCulture)} (including overdraft limit).");
        }

        var resTtl = request.ReservationTtl ?? TimeSpan.FromMinutes(30);
        var reservationId = $"res_{Guid.NewGuid():N}";

        var reservation = new TokenReservationModel(
            Id: reservationId,
            WalletId: wallet.Id,
            FeatureCode: request.FeatureCode,
            ReservedAmount: creditsNeeded,
            ConsumedAmount: 0,
            MachineId: request.MachineId,
            ClientRef: request.ClientRef,
            Status: "pending",
            IdempotencyKey: request.IdempotencyKey,
            ExpiresAt: now + resTtl,
            CreatedAt: now,
            UpdatedAt: now);

        await _store.SaveReservationAsync(reservation, ct).ConfigureAwait(false);

        var updatedWallet = await _store.UpdateWalletBalanceAtomicAsync(
            walletId: wallet.Id,
            balanceDelta: 0,
            reservedDelta: creditsNeeded,
            totalCreditsDelta: 0,
            ct: ct).ConfigureAwait(false);

        await _store.AddLedgerEntryAsync(new TokenLedgerEntryModel(
            Id: 0,
            WalletId: wallet.Id,
            ReservationId: reservationId,
            TransactionType: "reserve",
            Amount: creditsNeeded,
            BalanceAfter: updatedWallet.Balance,
            FeatureCode: request.FeatureCode,
            IdempotencyKey: request.IdempotencyKey,
            Timestamp: now,
            MetadataJson: "{\"action\":\"reserve_job\"}"), ct).ConfigureAwait(false);

        decimal remAvail = (updatedWallet.Balance + updatedWallet.OverdraftLimit) - updatedWallet.ReservedCredits;

        return new ReserveTokensResult(
            Success: true,
            ReservationId: reservationId,
            ReservedAmount: creditsNeeded,
            AvailableBalance: Math.Max(0, updatedWallet.Balance - updatedWallet.ReservedCredits),
            OverdraftRemaining: Math.Max(0, remAvail));
    }

    public async Task<HeartbeatTokensResult> HeartbeatTokensAsync(HeartbeatTokensRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ReservationId);

        var reservation = await _store.GetReservationByIdAsync(request.ReservationId, ct).ConfigureAwait(false);
        if (reservation is null)
        {
            return new HeartbeatTokensResult(false, 0, 0, 0, "Reservation not found.");
        }

        if (reservation.Status != "pending")
        {
            return new HeartbeatTokensResult(false, reservation.ConsumedAmount, 0, 0, $"Reservation is in status '{reservation.Status}'.");
        }

        var wallet = await _store.GetWalletByIdAsync(reservation.WalletId, ct).ConfigureAwait(false);
        if (wallet is null)
        {
            return new HeartbeatTokensResult(false, 0, 0, 0, "Wallet not found.");
        }

        decimal deltaCredits = await CalculateCreditsAsync(
            wallet.TenantId,
            reservation.FeatureCode,
            request.DeltaUnits,
            request.IsDurationMinutes,
            ct).ConfigureAwait(false);

        var now = _timeProvider.GetUtcNow();
        var updatedRes = reservation with
        {
            ConsumedAmount = reservation.ConsumedAmount + deltaCredits,
            ExpiresAt = now + TimeSpan.FromMinutes(15),
            UpdatedAt = now
        };

        await _store.SaveReservationAsync(updatedRes, ct).ConfigureAwait(false);

        decimal remainingReserved = Math.Max(0, updatedRes.ReservedAmount - updatedRes.ConsumedAmount);
        decimal avail = (wallet.Balance + wallet.OverdraftLimit) - wallet.ReservedCredits;

        return new HeartbeatTokensResult(
            Success: true,
            TotalConsumed: updatedRes.ConsumedAmount,
            RemainingReserved: remainingReserved,
            AvailableBalance: Math.Max(0, avail));
    }

    public async Task<CommitTokensResult> CommitTokensAsync(CommitTokensRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ReservationId);

        var reservation = await _store.GetReservationByIdAsync(request.ReservationId, ct).ConfigureAwait(false);
        if (reservation is null)
        {
            return new CommitTokensResult(false, 0, 0, 0, false, "Reservation not found.");
        }

        if (reservation.Status != "pending")
        {
            return new CommitTokensResult(false, reservation.ConsumedAmount, 0, 0, false, $"Reservation was already '{reservation.Status}'.");
        }

        var wallet = await _store.GetWalletByIdAsync(reservation.WalletId, ct).ConfigureAwait(false);
        if (wallet is null)
        {
            return new CommitTokensResult(false, 0, 0, 0, false, "Associated wallet not found.");
        }

        decimal actualCredits = await CalculateCreditsAsync(
            wallet.TenantId,
            reservation.FeatureCode,
            request.ActualTotalUnits,
            request.IsDurationMinutes,
            ct).ConfigureAwait(false);

        var now = _timeProvider.GetUtcNow();

        // Release previously reserved credits and deduct actual consumption from balance
        decimal balanceDelta = -actualCredits;
        decimal reservedDelta = -reservation.ReservedAmount;
        decimal released = Math.Max(0, reservation.ReservedAmount - actualCredits);

        var updatedWallet = await _store.UpdateWalletBalanceAtomicAsync(
            walletId: wallet.Id,
            balanceDelta: balanceDelta,
            reservedDelta: reservedDelta,
            totalCreditsDelta: 0,
            ct: ct).ConfigureAwait(false);

        var completedRes = reservation with
        {
            Status = "committed",
            ConsumedAmount = actualCredits,
            UpdatedAt = now
        };

        await _store.SaveReservationAsync(completedRes, ct).ConfigureAwait(false);

        await _store.AddLedgerEntryAsync(new TokenLedgerEntryModel(
            Id: 0,
            WalletId: wallet.Id,
            ReservationId: reservation.Id,
            TransactionType: "consume",
            Amount: actualCredits,
            BalanceAfter: updatedWallet.Balance,
            FeatureCode: reservation.FeatureCode,
            IdempotencyKey: request.IdempotencyKey,
            Timestamp: now,
            MetadataJson: $"{{\"consumed\":{actualCredits.ToString(CultureInfo.InvariantCulture)},\"released\":{released.ToString(CultureInfo.InvariantCulture)}}}"), ct).ConfigureAwait(false);

        bool isLowAlert = false;
        if (updatedWallet.ThresholdLowAlert.HasValue && updatedWallet.Balance <= updatedWallet.ThresholdLowAlert.Value)
        {
            isLowAlert = true;
        }

        return new CommitTokensResult(
            Success: true,
            ConsumedCredits: actualCredits,
            ReleasedCredits: released,
            FinalBalance: updatedWallet.Balance,
            IsLowBalanceAlert: isLowAlert);
    }

    public async Task<RollbackTokensResult> RollbackTokensAsync(RollbackTokensRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ReservationId);

        var reservation = await _store.GetReservationByIdAsync(request.ReservationId, ct).ConfigureAwait(false);
        if (reservation is null)
        {
            return new RollbackTokensResult(false, 0, 0, "Reservation not found.");
        }

        if (reservation.Status != "pending")
        {
            return new RollbackTokensResult(false, 0, 0, $"Reservation is already in status '{reservation.Status}'.");
        }

        var wallet = await _store.GetWalletByIdAsync(reservation.WalletId, ct).ConfigureAwait(false);
        if (wallet is null)
        {
            return new RollbackTokensResult(false, 0, 0, "Wallet not found.");
        }

        var now = _timeProvider.GetUtcNow();
        decimal released = reservation.ReservedAmount;

        var updatedWallet = await _store.UpdateWalletBalanceAtomicAsync(
            walletId: wallet.Id,
            balanceDelta: 0,
            reservedDelta: -released,
            totalCreditsDelta: 0,
            ct: ct).ConfigureAwait(false);

        var rolledBackRes = reservation with
        {
            Status = "rolled_back",
            UpdatedAt = now
        };

        await _store.SaveReservationAsync(rolledBackRes, ct).ConfigureAwait(false);

        await _store.AddLedgerEntryAsync(new TokenLedgerEntryModel(
            Id: 0,
            WalletId: wallet.Id,
            ReservationId: reservation.Id,
            TransactionType: "release",
            Amount: released,
            BalanceAfter: updatedWallet.Balance,
            FeatureCode: reservation.FeatureCode,
            IdempotencyKey: request.IdempotencyKey,
            Timestamp: now,
            MetadataJson: $"{{\"reason\":\"{request.Reason?.Replace("\"", "\\\"", StringComparison.Ordinal) ?? "Job cancelled/failed"}\"}}"), ct).ConfigureAwait(false);

        return new RollbackTokensResult(
            Success: true,
            ReleasedCredits: released,
            CurrentBalance: updatedWallet.Balance);
    }

    public async Task<TokenWalletBalanceDto?> GetBalanceAsync(string walletId, CancellationToken ct = default)
    {
        var wallet = await _store.GetWalletByIdAsync(walletId, ct).ConfigureAwait(false);
        if (wallet is null) return null;

        decimal avail = (wallet.Balance + wallet.OverdraftLimit) - wallet.ReservedCredits;
        bool isLow = wallet.ThresholdLowAlert.HasValue && wallet.Balance <= wallet.ThresholdLowAlert.Value;

        return new TokenWalletBalanceDto(
            WalletId: wallet.Id,
            Code: wallet.Code,
            Name: wallet.Name,
            Balance: wallet.Balance,
            ReservedCredits: wallet.ReservedCredits,
            OverdraftLimit: wallet.OverdraftLimit,
            AvailableCredits: Math.Max(0, avail),
            State: wallet.State,
            IsLowBalance: isLow);
    }

    public Task<IReadOnlyList<TokenWalletModel>> ListWalletsAsync(string? tenantId, CancellationToken ct = default) =>
        _store.ListWalletsAsync(tenantId, ct);

    public async Task<TokenRateModel> SetRateAsync(SetRateRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.TenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.FeatureCode);

        var now = _timeProvider.GetUtcNow();
        var existing = await _store.GetRateAsync(request.TenantId, request.ProductId, request.FeatureCode, ct).ConfigureAwait(false);

        var rate = new TokenRateModel(
            Id: existing?.Id ?? $"rate_{Guid.NewGuid():N}",
            TenantId: request.TenantId,
            ProductId: request.ProductId,
            FeatureCode: request.FeatureCode.Trim().ToUpperInvariant(),
            RatePerMinute: Math.Max(0, request.RatePerMinute),
            RatePerUnit: Math.Max(0, request.RatePerUnit),
            Description: request.Description,
            CreatedAt: existing?.CreatedAt ?? now,
            UpdatedAt: now);

        return await _store.SetRateAsync(rate, ct).ConfigureAwait(false);
    }

    public Task<IReadOnlyList<TokenRateModel>> ListRatesAsync(string? tenantId, string? productId, CancellationToken ct = default) =>
        _store.ListRatesAsync(tenantId, productId, ct);

    public Task<IReadOnlyList<TokenLedgerEntryModel>> ListLedgerAsync(string? walletId, int limit, CancellationToken ct = default) =>
        _store.ListLedgerEntriesAsync(walletId, limit, ct);

    private async Task<decimal> CalculateCreditsAsync(
        string tenantId,
        string featureCode,
        decimal units,
        bool isDurationMinutes,
        CancellationToken ct)
    {
        var rate = await _store.GetRateAsync(tenantId, null, featureCode, ct).ConfigureAwait(false);

        if (rate is null)
        {
            // Default rate if not explicitly configured: 1.0 credit per unit/minute
            return units;
        }

        decimal multiplier = isDurationMinutes ? rate.RatePerMinute : rate.RatePerUnit;
        if (multiplier <= 0)
        {
            multiplier = 1.0m;
        }

        return units * multiplier;
    }
}
