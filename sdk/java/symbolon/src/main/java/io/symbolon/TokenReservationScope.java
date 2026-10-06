package io.symbolon;

import io.symbolon.exceptions.SymbolonException;
import io.symbolon.models.*;

import java.util.Objects;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * Scoped handle for metered pay-as-you-go token consumption.
 * Implements AutoCloseable. If not committed explicitly, closing this scope
 * automatically rolls back the reserved credits to avoid credit leakage.
 */
public class TokenReservationScope implements AutoCloseable {

    private final SymbolonClient client;
    private final String walletId;
    private final String reservationId;
    private final String featureCode;
    private final double reservedAmount;
    private volatile double availableBalance;
    private final AtomicBoolean isCompleted = new AtomicBoolean(false);

    public TokenReservationScope(
            SymbolonClient client,
            String walletId,
            String reservationId,
            String featureCode,
            double reservedAmount,
            double availableBalance) {
        this.client = Objects.requireNonNull(client, "client cannot be null");
        this.walletId = Objects.requireNonNull(walletId, "walletId cannot be null");
        this.reservationId = Objects.requireNonNull(reservationId, "reservationId cannot be null");
        this.featureCode = featureCode;
        this.reservedAmount = reservedAmount;
        this.availableBalance = availableBalance;
    }

    public String getWalletId() {
        return walletId;
    }

    public String getReservationId() {
        return reservationId;
    }

    public String getFeatureCode() {
        return featureCode;
    }

    public double getReservedAmount() {
        return reservedAmount;
    }

    public double getAvailableBalance() {
        return availableBalance;
    }

    public boolean isCompleted() {
        return isCompleted.get();
    }

    public HeartbeatTokensResponse heartbeat(double deltaUnits, boolean isDurationMinutes) {
        if (isCompleted.get()) {
            throw new SymbolonException("INVALID_STATE", 400, "Token reservation '" + reservationId + "' has already been completed.", null);
        }
        var req = new HeartbeatTokensRequest(reservationId, deltaUnits, isDurationMinutes);
        var resp = client.heartbeatTokens(req);
        if (resp != null && Boolean.TRUE.equals(resp.success()) && resp.availableBalance() != null) {
            this.availableBalance = resp.availableBalance();
        }
        return resp;
    }

    public CommitTokensResponse commit(double actualUnits, boolean isDurationMinutes) {
        if (isCompleted.get()) {
            throw new SymbolonException("INVALID_STATE", 400, "Token reservation '" + reservationId + "' has already been completed.", null);
        }
        var req = new CommitTokensRequest(reservationId, actualUnits, isDurationMinutes);
        var resp = client.commitTokens(req);
        if (resp != null && Boolean.TRUE.equals(resp.success())) {
            this.isCompleted.set(true);
            if (resp.newBalance() != null) {
                this.availableBalance = resp.newBalance();
            }
        }
        return resp;
    }

    public RollbackTokensResponse rollback(String reason) {
        if (isCompleted.get()) {
            return new RollbackTokensResponse(true, 0.0, availableBalance, null);
        }
        var req = new RollbackTokensRequest(reservationId, reason);
        var resp = client.rollbackTokens(req);
        this.isCompleted.set(true);
        if (resp != null && Boolean.TRUE.equals(resp.success()) && resp.newBalance() != null) {
            this.availableBalance = resp.newBalance();
        }
        return resp;
    }

    @Override
    public void close() {
        if (!isCompleted.get()) {
            try {
                rollback("Scope closed without explicit commit");
            } catch (Exception ignored) {
                // Suppress rollback errors during close to preserve application exceptions
            }
        }
    }
}
