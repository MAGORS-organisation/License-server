"""Symbolon A/B Testing & Experimentation Engine for Python SDK (Phase 24 / AB-1..15).

Provides deterministic bucketing, experiment routing with sticky session guarantees,
and statistical analysis tools (Z-Test, Welch's t-test, confidence intervals).
"""

from __future__ import annotations

import hashlib
import math
import struct
from dataclasses import dataclass, field
from enum import Enum
from typing import Any, Dict, List, Optional, Tuple


class ExperimentStatus(str, Enum):
    DRAFT = "Draft"
    ACTIVE = "Active"
    PAUSED = "Paused"
    COMPLETED = "Completed"
    ROLLED_BACK = "RolledBack"


@dataclass
class ExperimentOverrides:
    lease_ttl_seconds: Optional[int] = None
    policy_rules_yaml: Optional[str] = None
    feature_flags: Dict[str, bool] = field(default_factory=dict)
    custom_metadata: Dict[str, str] = field(default_factory=dict)


@dataclass
class ExperimentVariant:
    variant_id: str
    name: str
    weight: int = 50
    is_control: bool = False
    overrides: ExperimentOverrides = field(default_factory=ExperimentOverrides)


@dataclass
class ExperimentTargeting:
    target_tenants: List[str] = field(default_factory=list)
    license_key_prefixes: List[str] = field(default_factory=list)
    allowed_sdk_versions: List[str] = field(default_factory=list)
    operating_systems: List[str] = field(default_factory=list)

    def matches(
        self,
        tenant_id: Optional[str],
        license_key: str,
        context: Optional[Dict[str, Any]] = None,
    ) -> bool:
        if self.target_tenants:
            if not tenant_id or tenant_id not in self.target_tenants:
                return False

        if self.license_key_prefixes:
            if not any(license_key.upper().startswith(p.upper()) for p in self.license_key_prefixes):
                return False

        if context:
            if self.allowed_sdk_versions:
                sdk = context.get("sdk_version") or context.get("sdkVersion")
                if not sdk or sdk not in self.allowed_sdk_versions:
                    return False

            if self.operating_systems:
                os_name = context.get("os_platform") or context.get("osPlatform") or context.get("os")
                if not os_name or not any(o.lower() in str(os_name).lower() for o in self.operating_systems):
                    return False

        return True


@dataclass
class ExperimentCircuitBreaker:
    max_error_rate: float = 0.05
    min_samples: int = 50
    auto_rollback: bool = True


@dataclass
class Experiment:
    id: str
    name: str
    description: str = ""
    status: ExperimentStatus = ExperimentStatus.DRAFT
    traffic_allocation: int = 100
    salt: str = ""
    promoted_variant_id: Optional[str] = None
    targeting: ExperimentTargeting = field(default_factory=ExperimentTargeting)
    variants: List[ExperimentVariant] = field(default_factory=list)
    circuit_breaker: ExperimentCircuitBreaker = field(default_factory=ExperimentCircuitBreaker)


@dataclass
class ExperimentEvaluationResult:
    experiment_id: str
    variant_id: str = "baseline"
    is_in_experiment: bool = False
    is_control: bool = True
    overrides: ExperimentOverrides = field(default_factory=ExperimentOverrides)

    @classmethod
    def not_in_experiment(cls, experiment_id: str) -> ExperimentEvaluationResult:
        return cls(experiment_id=experiment_id, variant_id="baseline", is_in_experiment=False, is_control=True)


# ==========================================
# Deterministic Bucket Router
# ==========================================

def calculate_bucket(license_key: str, machine_id: str, salt: str) -> int:
    """Calculates a deterministic, stateless bucket [0..99] for a given license key,

    machine ID, and experiment salt using SHA-256 little-endian uint32 modulo 100.
    Identical to .NET Symbolon.Domain.Experiments.DeterministicBucketRouter.CalculateBucket.
    """
    if not license_key or not license_key.strip():
        raise ValueError("license_key cannot be empty")
    if not machine_id or not machine_id.strip():
        raise ValueError("machine_id cannot be empty")

    input_str = f"{license_key.strip().upper()}:{machine_id.strip()}:{salt}".encode("utf-8")
    digest = hashlib.sha256(input_str).digest()
    val = struct.unpack("<I", digest[:4])[0]
    return int(val % 100)


def route_experiment(
    experiment: Experiment,
    tenant_id: Optional[str],
    license_key: str,
    machine_id: str,
    context: Optional[Dict[str, Any]] = None,
) -> ExperimentEvaluationResult:
    """Evaluates an experiment and assigns client to an experiment variant or baseline.

    Guarantees sticky session invariant: zero variant drift for fixed (license_key, machine_id, salt).
    """
    if not experiment:
        raise ValueError("experiment cannot be None")
    if not license_key or not license_key.strip():
        raise ValueError("license_key cannot be empty")
    if not machine_id or not machine_id.strip():
        raise ValueError("machine_id cannot be empty")

    # If completed and promoted, always route to promoted variant
    if experiment.status == ExperimentStatus.COMPLETED and experiment.promoted_variant_id:
        for v in experiment.variants:
            if v.variant_id == experiment.promoted_variant_id:
                return ExperimentEvaluationResult(
                    experiment_id=experiment.id,
                    variant_id=v.variant_id,
                    is_in_experiment=True,
                    is_control=v.is_control,
                    overrides=v.overrides,
                )

    if experiment.status != ExperimentStatus.ACTIVE:
        return ExperimentEvaluationResult.not_in_experiment(experiment.id)

    if not experiment.targeting.matches(tenant_id, license_key, context):
        return ExperimentEvaluationResult.not_in_experiment(experiment.id)

    bucket = calculate_bucket(license_key, machine_id, experiment.salt)
    if bucket >= experiment.traffic_allocation or experiment.traffic_allocation <= 0:
        return ExperimentEvaluationResult.not_in_experiment(experiment.id)

    if not experiment.variants:
        return ExperimentEvaluationResult.not_in_experiment(experiment.id)

    total_weight = sum(max(0, v.weight) for v in experiment.variants)
    if total_weight <= 0:
        return ExperimentEvaluationResult.not_in_experiment(experiment.id)

    scaled_point = (bucket * total_weight) // experiment.traffic_allocation

    accumulated = 0
    for v in experiment.variants:
        accumulated += max(0, v.weight)
        if scaled_point < accumulated:
            return ExperimentEvaluationResult(
                experiment_id=experiment.id,
                variant_id=v.variant_id,
                is_in_experiment=True,
                is_control=v.is_control,
                overrides=v.overrides,
            )

    fallback = experiment.variants[-1]
    return ExperimentEvaluationResult(
        experiment_id=experiment.id,
        variant_id=fallback.variant_id,
        is_in_experiment=True,
        is_control=fallback.is_control,
        overrides=fallback.overrides,
    )


# ==========================================
# Statistical Engine (Z-Test, Welch's t-test)
# ==========================================

Z95 = 1.959963984540054  # 95% two-tailed critical value


def erf(x: float) -> float:
    """Computes error function erf(x) using Abramowitz and Stegun 7.1.26 polynomial approximation (max error 1.5e-7)."""
    sign = -1.0 if x < 0 else 1.0
    x = abs(x)

    a1 = 0.254829592
    a2 = -0.284496736
    a3 = 1.421413741
    a4 = -1.453152027
    a5 = 1.061405429
    p = 0.3275911

    t = 1.0 / (1.0 + p * x)
    y = 1.0 - (((((a5 * t + a4) * t) + a3) * t + a2) * t + a1) * t * math.exp(-x * x)
    return sign * y


def normal_cdf(z: float) -> float:
    """Cumulative distribution function for standard normal distribution N(0,1)."""
    return 0.5 * (1.0 + erf(z / math.sqrt(2.0)))


def calculate_two_proportion_z_test(
    successes_a: int,
    trials_a: int,
    successes_b: int,
    trials_b: int,
) -> Tuple[float, float, float, float]:
    """Computes a Two-Proportion pooled Z-Test comparing success rates between Control (A) and Treatment (B).

    Returns (z_score, p_value, ci_lower, ci_upper).
    """
    if trials_a <= 0 or trials_b <= 0:
        return (0.0, 1.0, 0.0, 0.0)

    p_a = successes_a / trials_a
    p_b = successes_b / trials_b
    diff = p_b - p_a

    pooled_p = (successes_a + successes_b) / (trials_a + trials_b)
    pooled_var = pooled_p * (1.0 - pooled_p) * ((1.0 / trials_a) + (1.0 / trials_b))

    z_score = 0.0
    if pooled_var > 0:
        z_score = diff / math.sqrt(pooled_var)

    p_val = 2.0 * (1.0 - normal_cdf(abs(z_score)))
    p_val = max(0.0, min(1.0, p_val))

    se_diff = math.sqrt((p_a * (1.0 - p_a) / trials_a) + (p_b * (1.0 - p_b) / trials_b))
    ci_lower = diff - (Z95 * se_diff)
    ci_upper = diff + (Z95 * se_diff)

    return (z_score, p_val, ci_lower, ci_upper)


def calculate_welch_t_test(
    mean_a: float,
    std_dev_a: float,
    n_a: int,
    mean_b: float,
    std_dev_b: float,
    n_b: int,
) -> Tuple[float, float]:
    """Computes Welch's t-test for difference in continuous metric (e.g. latency) between Control (A) and Treatment (B).

    Returns (t_score, p_value).
    """
    if n_a <= 1 or n_b <= 1:
        return (0.0, 1.0)

    var_a = std_dev_a * std_dev_a
    var_b = std_dev_b * std_dev_b
    denom = math.sqrt((var_a / n_a) + (var_b / n_b))

    if denom <= 0:
        return (0.0, 1.0)

    t_score = (mean_b - mean_a) / denom
    p_val = 2.0 * (1.0 - normal_cdf(abs(t_score)))
    p_val = max(0.0, min(1.0, p_val))

    return (t_score, p_val)


def generate_statistical_report(
    experiment: Experiment,
    control_metrics: Dict[str, Any],
    treatment_metrics: Dict[str, Any],
) -> Dict[str, Any]:
    """Generates complete statistical report with automated recommendation."""
    trials_a = int(control_metrics.get("total_requests", 0))
    succ_a = int(control_metrics.get("successful_checkouts", 0))
    lat_a = float(control_metrics.get("average_latency_ms", 0.0))
    std_a = float(control_metrics.get("latency_std_dev", 0.0))

    trials_b = int(treatment_metrics.get("total_requests", 0))
    succ_b = int(treatment_metrics.get("successful_checkouts", 0))
    lat_b = float(treatment_metrics.get("average_latency_ms", 0.0))
    std_b = float(treatment_metrics.get("latency_std_dev", 0.0))

    z, p_val, ci_low, ci_high = calculate_two_proportion_z_test(succ_a, trials_a, succ_b, trials_b)
    t_score, t_p_val = calculate_welch_t_test(lat_a, std_a, trials_a, lat_b, std_b, trials_b)

    rate_a = succ_a / trials_a if trials_a > 0 else 0.0
    rate_b = succ_b / trials_b if trials_b > 0 else 0.0
    diff = rate_b - rate_a
    lat_diff = lat_b - lat_a

    is_significant = (p_val < 0.05) and (trials_a >= 30 and trials_b >= 30)

    err_rate_a = (trials_a - succ_a) / trials_a if trials_a > 0 else 0.0
    err_rate_b = (trials_b - succ_b) / trials_b if trials_b > 0 else 0.0

    if trials_a < 30 or trials_b < 30:
        recommendation = f"Nedostatok vzoriek (Control: {trials_a}, Treatment: {trials_b}). Minimum je 30 požiadaviek na variant."
    elif err_rate_b > err_rate_a and (err_rate_b - err_rate_a) > 0.03:
        recommendation = f"VAROVANIE: Variant 'Treatment' vykazuje zvýšenú chybovosť ({err_rate_b:.1%} vs. {err_rate_a:.1%}). Odporúča sa rollback."
    elif is_significant and diff > 0:
        recommendation = f"ODPORÚČANIE: Variant 'Treatment' vykazuje štatisticky signifikantné zlepšenie úspešnosti (+{diff:.1%}, p={p_val:.4f}). Bezpečné nasadiť na 100%."
    elif t_p_val < 0.05 and lat_diff < -5.0:
        recommendation = f"ODPORÚČANIE: Variant 'Treatment' signifikantne znižuje latenciu ({lat_diff:.1f} ms, p={t_p_val:.4f})."
    elif is_significant and diff < 0:
        recommendation = f"VAROVANIE: Variant 'Treatment' vykazuje signifikantný pokles úspešnosti ({diff:.1%}, p={p_val:.4f})."
    else:
        recommendation = f"Medzi variantmi zatiaľ nebol preukázaný štatisticky signifikantný rozdiel (p = {p_val:.4f}). Pokračujte v zbere dát."

    return {
        "experiment_id": experiment.id,
        "z_score": z,
        "p_value": p_val,
        "confidence_interval_lower": ci_low,
        "confidence_interval_upper": ci_high,
        "is_statistically_significant": is_significant,
        "difference_rate": diff,
        "latency_t_score": t_score,
        "latency_p_value": t_p_val,
        "latency_difference_ms": lat_diff,
        "recommendation": recommendation,
    }
