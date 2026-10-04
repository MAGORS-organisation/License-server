package io.symbolon.models;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;

/**
 * An individual variant branch in an experiment.
 */
@JsonIgnoreProperties(ignoreUnknown = true)
public record ExperimentVariant(
        String variantId,
        String name,
        int weight,
        boolean isControl,
        ExperimentOverrides overrides
) {
}
