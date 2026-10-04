package io.symbolon;

import io.symbolon.models.Experiment;
import io.symbolon.models.ExperimentEvaluationResult;
import io.symbolon.models.ExperimentOverrides;
import io.symbolon.models.ExperimentVariant;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.Map;

/**
 * Deterministic A/B experiment evaluation and stateless bucketing engine (AB-1..15).
 */
public final class ExperimentEngine {

    private ExperimentEngine() {
    }

    /**
     * Calculates a deterministic, stateless bucket [0..99] using SHA-256 little-endian uint32 modulo 100.
     * Guarantees zero-drift sticky session invariant across identical (licenseKey, machineId, salt).
     */
    public static int calculateBucket(String licenseKey, String machineId, String salt) {
        if (licenseKey == null || licenseKey.isBlank()) {
            throw new IllegalArgumentException("licenseKey cannot be empty");
        }
        if (machineId == null || machineId.isBlank()) {
            throw new IllegalArgumentException("machineId cannot be empty");
        }

        String inputStr = licenseKey.trim().toUpperCase() + ":" + machineId.trim() + ":" + (salt != null ? salt : "");
        try {
            MessageDigest digest = MessageDigest.getInstance("SHA-256");
            byte[] hash = digest.digest(inputStr.getBytes(StandardCharsets.UTF_8));
            long val = (hash[0] & 0xFFL)
                    | ((hash[1] & 0xFFL) << 8)
                    | ((hash[2] & 0xFFL) << 16)
                    | ((hash[3] & 0xFFL) << 24);
            return (int) (val % 100);
        } catch (NoSuchAlgorithmException e) {
            throw new IllegalStateException("SHA-256 not available", e);
        }
    }

    /**
     * Evaluates an experiment and assigns client to a variant or baseline.
     */
    public static ExperimentEvaluationResult routeExperiment(
            Experiment experiment,
            String tenantId,
            String licenseKey,
            String machineId,
            Map<String, String> context
    ) {
        if (experiment == null) {
            throw new IllegalArgumentException("experiment cannot be null");
        }

        ExperimentEvaluationResult baseline = new ExperimentEvaluationResult(
                experiment.id(),
                "baseline",
                false,
                true,
                new ExperimentOverrides()
        );

        // If completed and promoted, always route to promoted variant
        if ("Completed".equalsIgnoreCase(experiment.status()) && experiment.promotedVariantId() != null) {
            for (ExperimentVariant v : experiment.variants()) {
                if (v.variantId().equals(experiment.promotedVariantId())) {
                    return new ExperimentEvaluationResult(
                            experiment.id(),
                            v.variantId(),
                            true,
                            v.isControl(),
                            v.overrides() != null ? v.overrides() : new ExperimentOverrides()
                    );
                }
            }
        }

        if (!"Active".equalsIgnoreCase(experiment.status())) {
            return baseline;
        }

        if (experiment.targeting() != null && !experiment.targeting().matches(tenantId, licenseKey, context)) {
            return baseline;
        }

        int bucket = calculateBucket(licenseKey, machineId, experiment.salt());
        if (bucket >= experiment.trafficAllocation() || experiment.trafficAllocation() <= 0) {
            return baseline;
        }

        if (experiment.variants() == null || experiment.variants().isEmpty()) {
            return baseline;
        }

        int totalWeight = 0;
        for (ExperimentVariant v : experiment.variants()) {
            totalWeight += Math.max(0, v.weight());
        }
        if (totalWeight <= 0) {
            return baseline;
        }

        int scaledPoint = (bucket * totalWeight) / experiment.trafficAllocation();
        int accumulated = 0;
        for (ExperimentVariant v : experiment.variants()) {
            accumulated += Math.max(0, v.weight());
            if (scaledPoint < accumulated) {
                return new ExperimentEvaluationResult(
                        experiment.id(),
                        v.variantId(),
                        true,
                        v.isControl(),
                        v.overrides() != null ? v.overrides() : new ExperimentOverrides()
                );
            }
        }

        ExperimentVariant fallback = experiment.variants().get(experiment.variants().size() - 1);
        return new ExperimentEvaluationResult(
                experiment.id(),
                fallback.variantId(),
                true,
                fallback.isControl(),
                fallback.overrides() != null ? fallback.overrides() : new ExperimentOverrides()
        );
    }
}
