package io.symbolon.models;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;

@JsonIgnoreProperties(ignoreUnknown = true)
public record TokenWalletBalance(
        String walletId,
        String walletCode,
        String walletName,
        Double totalCredits,
        Double balance,
        Double reservedCredits,
        Double availableBalance,
        Double overdraftLimit,
        String state,
        Boolean isLowBalance,
        String expiresAt
) {}
