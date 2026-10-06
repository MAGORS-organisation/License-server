package io.symbolon.models;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;

@JsonIgnoreProperties(ignoreUnknown = true)
public record RollbackTokensResponse(
        Boolean success,
        Double restoredCredits,
        Double newBalance,
        String failureReason
) {}
