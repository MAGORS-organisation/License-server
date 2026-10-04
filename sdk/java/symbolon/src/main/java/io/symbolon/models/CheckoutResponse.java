package io.symbolon.models;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;

import java.time.Instant;
import java.util.List;

/**
 * Server response received upon successful seat acquisition.
 */
@JsonIgnoreProperties(ignoreUnknown = true)
public record CheckoutResponse(
        String leaseId,
        String token,
        int seat,
        Instant expiresAt,
        List<String> entitlements,
        Boolean overage
) {
    public boolean isOverage() {
        return Boolean.TRUE.equals(overage);
    }
}
