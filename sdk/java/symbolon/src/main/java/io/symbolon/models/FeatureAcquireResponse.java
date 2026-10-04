package io.symbolon.models;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;

import java.time.Instant;

/**
 * Result of acquiring an add-on module.
 */
@JsonIgnoreProperties(ignoreUnknown = true)
public record FeatureAcquireResponse(
        boolean success,
        String featureCode,
        int inUse,
        int maxSeats,
        Instant expiresAt
) {
}
