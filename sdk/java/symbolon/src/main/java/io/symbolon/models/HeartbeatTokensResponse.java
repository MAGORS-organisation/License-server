package io.symbolon.models;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;

@JsonIgnoreProperties(ignoreUnknown = true)
public record HeartbeatTokensResponse(
        Boolean success,
        Double totalConsumed,
        Double remainingReserved,
        Double availableBalance,
        String failureReason
) {}
