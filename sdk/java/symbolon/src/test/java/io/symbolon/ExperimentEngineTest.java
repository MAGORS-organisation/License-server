package io.symbolon;

import io.symbolon.models.*;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;

import java.util.List;
import java.util.Map;

import static org.junit.jupiter.api.Assertions.*;

class ExperimentEngineTest {

    @Test
    @DisplayName("CalculateBucket produces deterministic bucket between 0 and 99")
    void testCalculateBucketDeterminism() {
        String key = "SYM-4K7QT-9M2XA-B8ZH3-P6RWY-C1J8N";
        String machine = "WORKSTATION-01";
        String salt = "exp_salt_v1";

        int b1 = ExperimentEngine.calculateBucket(key, machine, salt);
        int b2 = ExperimentEngine.calculateBucket(key, machine, salt);

        assertEquals(b1, b2);
        assertTrue(b1 >= 0 && b1 < 100);
    }

    @Test
    @DisplayName("CalculateBucket is case-insensitive for license key and trims whitespace")
    void testCalculateBucketNormalization() {
        int b1 = ExperimentEngine.calculateBucket("sym-key-1", "mach-1", "salt");
        int b2 = ExperimentEngine.calculateBucket("  SYM-KEY-1  ", "mach-1", "salt");
        assertEquals(b1, b2);
    }

    @Test
    @DisplayName("RouteExperiment assigns active variants with zero-drift sticky session")
    void testRouteExperimentStickySession() {
        ExperimentVariant control = new ExperimentVariant(
                "var_control", "Control (A)", 50, true, new ExperimentOverrides()
        );
        ExperimentVariant treatment = new ExperimentVariant(
                "var_treatment", "Treatment (B)", 50, false,
                new ExperimentOverrides(30L, null, Map.of("turbo", true), Map.of())
        );

        Experiment exp = new Experiment(
                "exp_lease_ttl",
                "Lease TTL A/B Test",
                "Testing 30s vs 60s lease TTL",
                "Active",
                100,
                "salt_test_1",
                null,
                new ExperimentTargeting(List.of(), List.of(), List.of(), List.of()),
                List.of(control, treatment)
        );

        // Multiple routing calls for the same machine must never drift
        ExperimentEvaluationResult res1 = ExperimentEngine.routeExperiment(exp, "tenant-1", "KEY-AAA", "MACH-01", Map.of());
        ExperimentEvaluationResult res2 = ExperimentEngine.routeExperiment(exp, "tenant-1", "KEY-AAA", "MACH-01", Map.of());

        assertEquals(res1.variantId(), res2.variantId());
        assertEquals(res1.isInExperiment(), res2.isInExperiment());
        assertTrue(res1.isInExperiment());
    }

    @Test
    @DisplayName("RouteExperiment returns baseline if experiment is paused or draft")
    void testInactiveExperimentRoutesToBaseline() {
        Experiment exp = new Experiment(
                "exp_paused", "Paused Exp", "", "Paused", 100, "salt", null, null, List.of()
        );
        ExperimentEvaluationResult res = ExperimentEngine.routeExperiment(exp, "tenant-1", "KEY-AAA", "MACH-01", Map.of());
        assertFalse(res.isInExperiment());
        assertEquals("baseline", res.variantId());
    }

    @Test
    @DisplayName("RouteExperiment respects tenant targeting")
    void testTenantTargeting() {
        ExperimentVariant v = new ExperimentVariant("v1", "V1", 100, false, new ExperimentOverrides());
        Experiment exp = new Experiment(
                "exp_targeted", "Targeted", "", "Active", 100, "salt", null,
                new ExperimentTargeting(List.of("tenant-vip"), List.of(), List.of(), List.of()),
                List.of(v)
        );

        ExperimentEvaluationResult resDenied = ExperimentEngine.routeExperiment(exp, "tenant-regular", "KEY-AAA", "MACH-01", Map.of());
        assertFalse(resDenied.isInExperiment());

        ExperimentEvaluationResult resAllowed = ExperimentEngine.routeExperiment(exp, "tenant-vip", "KEY-AAA", "MACH-01", Map.of());
        assertTrue(resAllowed.isInExperiment());
        assertEquals("v1", resAllowed.variantId());
    }
}
