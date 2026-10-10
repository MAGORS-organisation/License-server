using System.Diagnostics;
using Achilles.Protocol;
using Achilles.Protocol.Tracing;

namespace Achilles.Client;

/// <summary>
/// Scoped handle for metered pay-as-you-go token consumption.
/// Conforms to IAsyncDisposable and IDisposable. If not committed explicitly,
/// disposing this scope automatically rolls back the reserved credits to avoid credit leakage.
/// </summary>
public sealed class TokenReservationScope : IAsyncDisposable, IDisposable
{
    private readonly AchillesClient _client;
    private bool _isCompleted;
    private bool _isDisposed;

    public string WalletId { get; }
    public string ReservationId { get; }
    public string FeatureCode { get; }
    public decimal ReservedAmount { get; }
    public decimal AvailableBalance { get; private set; }
    public bool IsCompleted => _isCompleted;

    internal TokenReservationScope(
        AchillesClient client,
        string walletId,
        string reservationId,
        string featureCode,
        decimal reservedAmount,
        decimal availableBalance)
    {
        _client = client;
        WalletId = walletId;
        ReservationId = reservationId;
        FeatureCode = featureCode;
        ReservedAmount = reservedAmount;
        AvailableBalance = availableBalance;
    }

    /// <summary>
    /// Reports incremental consumption for long-running metered operations.
    /// </summary>
    public async Task<HeartbeatTokensResponseDto> HeartbeatAsync(decimal deltaUnits, bool isDurationMinutes = false, CancellationToken ct = default)
    {
        ThrowIfCompleted();

        var request = new HeartbeatTokensRequestDto
        {
            ReservationId = ReservationId,
            DeltaUnits = deltaUnits,
            IsDurationMinutes = isDurationMinutes
        };

        var response = await _client.HeartbeatTokensAsync(request, ct).ConfigureAwait(false);
        if (response.Success)
        {
            AvailableBalance = response.AvailableBalance;
        }

        return response;
    }

    /// <summary>
    /// Commits the metered reservation with the actual units consumed, refunding any unused credits.
    /// Marks the scope as completed.
    /// </summary>
    public async Task<CommitTokensResponseDto> CommitAsync(decimal actualUnits, bool isDurationMinutes = false, CancellationToken ct = default)
    {
        ThrowIfCompleted();

        var request = new CommitTokensRequestDto
        {
            ReservationId = ReservationId,
            ActualUnits = actualUnits,
            IsDurationMinutes = isDurationMinutes
        };

        var response = await _client.CommitTokensAsync(request, ct).ConfigureAwait(false);
        if (response.Success)
        {
            _isCompleted = true;
            AvailableBalance = response.NewBalance;
        }

        return response;
    }

    /// <summary>
    /// Aborts the metered operation and restores all reserved credits back to the wallet.
    /// </summary>
    public async Task<RollbackTokensResponseDto> RollbackAsync(string reason = "Aborted by client", CancellationToken ct = default)
    {
        if (_isCompleted)
        {
            return new RollbackTokensResponseDto
            {
                Success = true,
                RestoredCredits = 0,
                NewBalance = AvailableBalance
            };
        }

        var request = new RollbackTokensRequestDto
        {
            ReservationId = ReservationId,
            Reason = reason
        };

        var response = await _client.RollbackTokensAsync(request, ct).ConfigureAwait(false);
        _isCompleted = true;
        if (response.Success)
        {
            AvailableBalance = response.NewBalance;
        }

        return response;
    }

    private void ThrowIfCompleted()
    {
        if (_isCompleted)
        {
            throw new InvalidOperationException($"Token reservation '{ReservationId}' has already been completed.");
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (!_isCompleted)
        {
#pragma warning disable CA1031 // Suppress rollback errors during dispose to preserve original application exception stack
            try
            {
                await RollbackAsync("Scope disposed without explicit commit", CancellationToken.None).ConfigureAwait(false);
            }
            catch
            {
                // Suppress rollback errors during dispose to preserve original application exception stack
            }
#pragma warning restore CA1031
        }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        if (!_isCompleted)
        {
#pragma warning disable CA1031 // Suppress rollback errors during synchronous dispose
            try
            {
                RollbackAsync("Scope disposed synchronously without explicit commit", CancellationToken.None)
                    .GetAwaiter()
                    .GetResult();
            }
            catch
            {
                // Suppress rollback errors during synchronous dispose
            }
#pragma warning restore CA1031
        }
    }
}
