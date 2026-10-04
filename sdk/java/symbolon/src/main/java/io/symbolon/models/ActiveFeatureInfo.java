package io.symbolon.models;

import java.time.Instant;

/**
 * Information about currently held add-on features.
 */
public record ActiveFeatureInfo(
        String featureCode,
        String version,
        Instant expiresAt
) {
}
