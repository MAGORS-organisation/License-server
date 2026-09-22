namespace Symbolon.Domain.Tokens;

/// <summary>
/// Domain engine orchestrating token consumption, credit allocations, metered reservations, and rate evaluations.
/// </summary>
public interface ITokenEngine
{
    Task<TokenWalletModel> CreateWalletAsync(CreateWalletRequest request, CancellationToken ct = default);
    Task<TokenWalletModel> CreditWalletAsync(CreditWalletRequest request, CancellationToken ct = default);
    Task<ReserveTokensResult> ReserveTokensAsync(ReserveTokensRequest request, CancellationToken ct = default);
    Task<HeartbeatTokensResult> HeartbeatTokensAsync(HeartbeatTokensRequest request, CancellationToken ct = default);
    Task<CommitTokensResult> CommitTokensAsync(CommitTokensRequest request, CancellationToken ct = default);
    Task<RollbackTokensResult> RollbackTokensAsync(RollbackTokensRequest request, CancellationToken ct = default);
    Task<TokenWalletBalanceDto?> GetBalanceAsync(string walletId, CancellationToken ct = default);
    Task<IReadOnlyList<TokenWalletModel>> ListWalletsAsync(string? tenantId, CancellationToken ct = default);
    Task<TokenRateModel> SetRateAsync(SetRateRequest request, CancellationToken ct = default);
    Task<IReadOnlyList<TokenRateModel>> ListRatesAsync(string? tenantId, string? productId, CancellationToken ct = default);
    Task<IReadOnlyList<TokenLedgerEntryModel>> ListLedgerAsync(string? walletId, int limit, CancellationToken ct = default);
}
