package io.symbolon.models;

import com.fasterxml.jackson.annotation.JsonIgnoreProperties;
import com.fasterxml.jackson.annotation.JsonInclude;

import java.util.Map;

/**
 * Client overrides imposed by an A/B experiment variant.
 */
@JsonIgnoreProperties(ignoreUnknown = true)
@JsonInclude(JsonInclude.Include.NON_NULL)
public record ExperimentOverrides(
        Long leaseTtlSeconds,
        String policyRulesYaml,
        Map<String, Boolean> featureFlags,
        Map<String, String> customMetadata
) {
    public ExperimentOverrides() {
        this(null, null, Map.of(), Map.of());
    }
}
