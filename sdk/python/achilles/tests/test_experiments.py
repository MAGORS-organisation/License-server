"""Unit tests for Symbolon A/B Testing & Experimentation Engine (Phase 24 / AB-1..15)."""

import math
import unittest
from symbolon.experiment import (
    Experiment,
    ExperimentCircuitBreaker,
    ExperimentOverrides,
    ExperimentStatus,
    ExperimentTargeting,
    ExperimentVariant,
    calculate_bucket,
    calculate_two_proportion_z_test,
    calculate_welch_t_test,
    erf,
    generate_statistical_report,
    normal_cdf,
    route_experiment,
)


class TestExperimentationEngine(unittest.TestCase):
    def test_calculate_bucket_range_and_stability(self):
        """Validates calculate_bucket is in [0..99] and perfectly stable."""
        for i in range(200):
            b1 = calculate_bucket(f"SYM-KEY-{i}", f"hw-node-{i}", "salt-xyz")
            b2 = calculate_bucket(f"SYM-KEY-{i}", f"hw-node-{i}", "salt-xyz")
            self.assertGreaterEqual(b1, 0)
            self.assertLess(b1, 100)
            self.assertEqual(b1, b2, "Must be strictly deterministic")

    def test_calculate_bucket_cross_platform_parity(self):
        """Cross-platform parity test with .NET SHA-256 little-endian uint32 implementation."""
        # SYM-PRO-2026-X:node-01:salt99
        # SHA256 input is "SYM-PRO-2026-X:node-01:salt99"
        b = calculate_bucket("SYM-PRO-2026-X", "node-01", "salt99")
        self.assertIsInstance(b, int)
        self.assertGreaterEqual(b, 0)
        self.assertLess(b, 100)

        # Case insensitivity of license key
        b_upper = calculate_bucket("sym-pro-2026-x", "node-01", "salt99")
        self.assertEqual(b, b_upper)

    def test_sticky_session_zero_drift(self):
        """Sticky session invariant: client never drifts across repeat evaluations."""
        exp = Experiment(
            id="exp_test_ttl",
            name="TTL Test",
            status=ExperimentStatus.ACTIVE,
            traffic_allocation=100,
            salt="stable_salt_2026",
            variants=[
                ExperimentVariant(variant_id="A", name="Control", weight=50, is_control=True),
                ExperimentVariant(variant_id="B", name="Treatment", weight=50, is_control=False),
            ],
        )

        res1 = route_experiment(exp, "ten_acme", "SYM-KEY-001", "mach-001")
        for _ in range(50):
            res_repeat = route_experiment(exp, "ten_acme", "SYM-KEY-001", "mach-001")
            self.assertEqual(res1.variant_id, res_repeat.variant_id)
            self.assertEqual(res1.is_in_experiment, res_repeat.is_in_experiment)

    def test_uniform_traffic_distribution(self):
        """Simulates 5,000 clients and verifies 50/50 traffic split is within reasonable bounds."""
        exp = Experiment(
            id="exp_uniform",
            name="Uniformity Test",
            status=ExperimentStatus.ACTIVE,
            traffic_allocation=100,
            salt="salt_chi_sq",
            variants=[
                ExperimentVariant(variant_id="A", name="Control", weight=50, is_control=True),
                ExperimentVariant(variant_id="B", name="Treatment", weight=50, is_control=False),
            ],
        )

        counts = {"A": 0, "B": 0}
        total = 5000
        for i in range(total):
            res = route_experiment(exp, "ten_test", f"SYM-LIC-{i}", f"mach-{i * 7}")
            counts[res.variant_id] += 1

        # Each variant should be near 50% (between 46% and 54%)
        pct_a = counts["A"] / total
        pct_b = counts["B"] / total
        self.assertAlmostEqual(pct_a, 0.50, delta=0.04)
        self.assertAlmostEqual(pct_b, 0.50, delta=0.04)

    def test_status_lifecycle_and_promotion(self):
        """Tests Draft, Paused, and Promoted status behavior."""
        exp = Experiment(
            id="exp_lifecycle",
            name="Lifecycle Test",
            status=ExperimentStatus.DRAFT,
            traffic_allocation=100,
            salt="salt1",
            variants=[
                ExperimentVariant(variant_id="A", name="Control", weight=50, is_control=True),
                ExperimentVariant(variant_id="B", name="Treatment", weight=50, is_control=False),
            ],
        )

        # Draft -> baseline
        res_draft = route_experiment(exp, "ten_1", "SYM-KEY-1", "mach-1")
        self.assertFalse(res_draft.is_in_experiment)

        # Paused -> baseline
        exp.status = ExperimentStatus.PAUSED
        res_paused = route_experiment(exp, "ten_1", "SYM-KEY-1", "mach-1")
        self.assertFalse(res_paused.is_in_experiment)

        # Active -> in experiment
        exp.status = ExperimentStatus.ACTIVE
        res_active = route_experiment(exp, "ten_1", "SYM-KEY-1", "mach-1")
        self.assertTrue(res_active.is_in_experiment)

        # Completed with promoted variant -> always returns promoted variant
        exp.status = ExperimentStatus.COMPLETED
        exp.promoted_variant_id = "B"
        for i in range(20):
            res_promoted = route_experiment(exp, "ten_1", f"SYM-KEY-{i}", f"mach-{i}")
            self.assertTrue(res_promoted.is_in_experiment)
            self.assertEqual(res_promoted.variant_id, "B")

    def test_targeting_rules(self):
        """Tests tenant, prefix, and context targeting."""
        exp = Experiment(
            id="exp_targeted",
            name="Targeted Test",
            status=ExperimentStatus.ACTIVE,
            traffic_allocation=100,
            salt="salt_target",
            targeting=ExperimentTargeting(
                target_tenants=["ten_acme"],
                license_key_prefixes=["SYM-ENT"],
                allowed_sdk_versions=["1.0.0"],
                operating_systems=["linux", "windows"],
            ),
            variants=[
                ExperimentVariant(variant_id="A", name="Control", weight=50, is_control=True),
                ExperimentVariant(variant_id="B", name="Treatment", weight=50, is_control=False),
            ],
        )

        # Non-matching tenant
        res_wrong_tenant = route_experiment(exp, "ten_other", "SYM-ENT-001", "m1")
        self.assertFalse(res_wrong_tenant.is_in_experiment)

        # Non-matching key prefix
        res_wrong_prefix = route_experiment(exp, "ten_acme", "SYM-STD-001", "m1")
        self.assertFalse(res_wrong_prefix.is_in_experiment)

        # Non-matching SDK version
        ctx_wrong_sdk = {"sdk_version": "0.9.0", "os_platform": "linux"}
        res_wrong_sdk = route_experiment(exp, "ten_acme", "SYM-ENT-001", "m1", ctx_wrong_sdk)
        self.assertFalse(res_wrong_sdk.is_in_experiment)

        # Valid match
        ctx_valid = {"sdk_version": "1.0.0", "os_platform": "Windows 11"}
        res_valid = route_experiment(exp, "ten_acme", "SYM-ENT-001", "m1", ctx_valid)
        self.assertTrue(res_valid.is_in_experiment)

    def test_erf_and_normal_cdf(self):
        """Verifies Abramowitz & Stegun erf precision against math.erf."""
        test_points = [-3.0, -1.96, -1.0, 0.0, 1.0, 1.96, 3.0]
        for x in test_points:
            expected = math.erf(x)
            actual = erf(x)
            self.assertAlmostEqual(actual, expected, places=5)

        # Standard normal CDF values
        self.assertAlmostEqual(normal_cdf(0.0), 0.50, places=5)
        self.assertAlmostEqual(normal_cdf(1.95996), 0.975, places=3)
        self.assertAlmostEqual(normal_cdf(-1.95996), 0.025, places=3)

    def test_two_proportion_z_test(self):
        """Tests pooled two-proportion Z-test for statistical significance."""
        # 1000 trials, Control has 90% success, Treatment has 95% success
        z, p_val, ci_low, ci_high = calculate_two_proportion_z_test(900, 1000, 950, 1000)
        self.assertGreater(z, 3.0)  # Highly significant Z > 3
        self.assertLess(p_val, 0.01)  # p < 0.01
        self.assertGreater(ci_low, 0.02)
        self.assertLess(ci_high, 0.08)

        # Identical rates -> Z should be 0, p-value should be 1.0
        z0, p0, _, _ = calculate_two_proportion_z_test(500, 1000, 500, 1000)
        self.assertAlmostEqual(z0, 0.0, places=4)
        self.assertAlmostEqual(p0, 1.0, places=4)

    def test_welch_t_test_latency(self):
        """Tests Welch's t-test on continuous latency difference."""
        # Control: 100ms avg, std 10ms, N=500
        # Treatment: 80ms avg, std 10ms, N=500 -> significant reduction
        t_score, p_val = calculate_welch_t_test(100.0, 10.0, 500, 80.0, 10.0, 500)
        self.assertLess(t_score, -20.0)
        self.assertLess(p_val, 0.001)

    def test_generate_statistical_report(self):
        """Tests automated recommendation logic in statistical report."""
        exp = Experiment(id="exp_rep", name="Report Exp")

        # Insufficient samples (< 30)
        ctrl_small = {"total_requests": 15, "successful_checkouts": 14}
        treat_small = {"total_requests": 15, "successful_checkouts": 15}
        rep1 = generate_statistical_report(exp, ctrl_small, treat_small)
        self.assertIn("Nedostatok vzoriek", rep1["recommendation"])

        # Significant improvement (Control 90%, Treatment 96%, N=1000)
        ctrl_large = {"total_requests": 1000, "successful_checkouts": 900, "average_latency_ms": 50.0, "latency_std_dev": 5.0}
        treat_large = {"total_requests": 1000, "successful_checkouts": 960, "average_latency_ms": 50.0, "latency_std_dev": 5.0}
        rep2 = generate_statistical_report(exp, ctrl_large, treat_large)
        self.assertTrue(rep2["is_statistically_significant"])
        self.assertIn("ODPORÚČANIE", rep2["recommendation"])
        self.assertIn("zlepšenie úspešnosti", rep2["recommendation"])


if __name__ == "__main__":
    unittest.main()
