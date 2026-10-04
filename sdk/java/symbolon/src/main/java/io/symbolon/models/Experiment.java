package io.symbolon.models;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;

import java.util.List;

/**
 * Complete A/B experiment definition.
 */
@JsonIgnoreProperties(ignoreUnknown = true)
public record Experiment(
        String id,
        String name,
        String description,
        String status,
        int trafficAllocation,
        String salt,
        String promotedVariantId,
        ExperimentTargeting targeting,
        List<ExperimentVariant> variants
) {
}
