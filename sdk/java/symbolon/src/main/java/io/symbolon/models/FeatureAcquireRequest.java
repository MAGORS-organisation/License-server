package io.symbolon.models;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;
import com.fasterxml.jackson.annotation.JsonInclude;

/**
 * Dynamic feature add-on acquisition request.
 */
@JsonIgnoreProperties(ignoreUnknown = true)
@JsonInclude(JsonInclude.Include.NON_NULL)
public record FeatureAcquireRequest(
        String featureCode,
        String version
) {
    public FeatureAcquireRequest(String featureCode) {
        this(featureCode, null);
    }
}
