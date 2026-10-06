package io.symbolon.models;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;

@JsonIgnoreProperties(ignoreUnknown = true)
public record CommitTokensResponse(
        Boolean success,
        Double consumedCredits,
        Double refundedCredits,
        Double newBalance,
        String failureReason
) {}
