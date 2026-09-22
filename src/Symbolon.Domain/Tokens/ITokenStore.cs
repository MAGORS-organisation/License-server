namespace Symbolon.Domain.Tokens;

/// <summary>
/// Persistence contract for token wallets, consumption rates, reservations, and ledger entries.
/// </summary>
public interface ITokenStore
{
    Task<TokenWalletModel> CreateWalletAsync(TokenWalletModel wallet, CancellationToken ct = default);
    Task<TokenWalletModel?> GetWalletByIdAsync(string walletId, CancellationToken ct = default);
    Task<TokenWalletModel?> GetWalletByCodeAsync(string tenantId, string code, CancellationToken ct = default);
    Task<IReadOnlyList<TokenWalletModel>> ListWalletsAsync(string? tenantId, CancellationToken ct = default);
    Task<TokenWalletModel> UpdateWalletAsync(TokenWalletModel wallet, CancellationToken ct = default);

    Task<TokenRateModel> SetRateAsync(TokenRateModel rate, CancellationToken ct = default);
    Task<TokenRateModel?> GetRateAsync(string tenantId, string? productId, string featureCode, CancellationToken ct = default);
    Task<IReadOnlyList<TokenRateModel>> ListRatesAsync(string? tenantId, string? productId, CancellationToken ct = default);

    Task<TokenReservationModel?> GetReservationByIdAsync(string reservationId, CancellationToken ct = default);
    Task<TokenReservationModel?> FindReservationByIdempotencyAsync(string idempotencyKey, CancellationToken ct = default);
    Task<TokenReservationModel> SaveReservationAsync(TokenReservationModel reservation, CancellationToken ct = default);

    Task<TokenLedgerEntryModel> AddLedgerEntryAsync(TokenLedgerEntryModel entry, CancellationToken ct = default);
    Task<IReadOnlyList<TokenLedgerEntryModel>> ListLedgerEntriesAsync(string? walletId, int limit, CancellationToken ct = default);

    /// <summary>
    /// Atomically updates wallet balance, reserved credits, and total credits.
    /// </summary>
    Task<TokenWalletModel> UpdateWalletBalanceAtomicAsync(
        string walletId,
        decimal balanceDelta,
        decimal reservedDelta,
        decimal totalCreditsDelta,
        CancellationToken ct = default);
}
