package io.symbolon.models;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;

/**
 * Result of client routing through an A/B experiment.
 */
@JsonIgnoreProperties(ignoreUnknown = true)
public record ExperimentEvaluationResult(
        String experimentId,
        String variantId,
        boolean isInExperiment,
        boolean isControl,
        ExperimentOverrides overrides
) {
}
