package io.symbolon.models;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;

/**
 * Result of releasing an add-on module.
 */
@JsonIgnoreProperties(ignoreUnknown = true)
public record FeatureReleaseResponse(
        boolean success,
        String featureCode
) {
}
