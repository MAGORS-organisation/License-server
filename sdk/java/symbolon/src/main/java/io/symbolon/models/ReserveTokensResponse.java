package io.symbolon.models;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;

@JsonIgnoreProperties(ignoreUnknown = true)
public record ReserveTokensResponse(
        Boolean success,
        String reservationId,
        Double reservedAmount,
        Double availableBalance,
        Double overdraftRemaining,
        String failureReason
) {}
