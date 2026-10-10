"""Symbolon Post-Quantum Era Suite for Python SDK (Phase 25 / Míľnik M7, §13.5).

Provides PQC profile enforcement, algorithm definitions (FIPS 203, FIPS 204, FIPS 205),
and quantum vulnerability auditing (CNSA 2.0 / EU NIS 2 compliance).
"""

from __future__ import annotations

from dataclasses import dataclass, field
from datetime import datetime, timezone
from enum import Enum
from typing import Any, Dict, List, Optional


class PqcProfile(str, Enum):
    HYBRID_V1 = "hybrid-v1"
    PQC_STRICT = "pqc-strict"


class PqcAlgorithm:
    # Classical
    ES256 = "ES256"

    # FIPS 204: ML-DSA (Module-Lattice Digital Signatures)
    ML_DSA_44 = "ML-DSA-44"
    ML_DSA_65 = "ML-DSA-65"
    ML_DSA_87 = "ML-DSA-87"

    # FIPS 203: ML-KEM (Module-Lattice Key Encapsulation)
    ML_KEM_512 = "ML-KEM-512"
    ML_KEM_768 = "ML-KEM-768"
    ML_KEM_1024 = "ML-KEM-1024"

    # FIPS 205: SLH-DSA (Stateless Hash-Based Signatures / SPHINCS+)
    SLH_DSA_SHA2_128S = "SLH-DSA-SHA2-128s"
    SLH_DSA_SHA2_128F = "SLH-DSA-SHA2-128f"
    SLH_DSA_SHAKE_128S = "SLH-DSA-SHAKE-128s"


POST_QUANTUM_ALGORITHMS = {
    PqcAlgorithm.ML_DSA_44,
    PqcAlgorithm.ML_DSA_65,
    PqcAlgorithm.ML_DSA_87,
    PqcAlgorithm.ML_KEM_512,
    PqcAlgorithm.ML_KEM_768,
    PqcAlgorithm.ML_KEM_1024,
    PqcAlgorithm.SLH_DSA_SHA2_128S,
    PqcAlgorithm.SLH_DSA_SHA2_128F,
    PqcAlgorithm.SLH_DSA_SHAKE_128S,
}

CNSA2_COMPLIANT_ALGORITHMS = {
    PqcAlgorithm.ML_DSA_65,
    PqcAlgorithm.ML_DSA_87,
    PqcAlgorithm.ML_KEM_768,
    PqcAlgorithm.ML_KEM_1024,
    PqcAlgorithm.SLH_DSA_SHA2_128S,
    PqcAlgorithm.SLH_DSA_SHAKE_128S,
}


def is_post_quantum(alg: str) -> bool:
    """Returns True if the algorithm is quantum-safe according to NIST PQC standards."""
    return alg in POST_QUANTUM_ALGORITHMS


def is_cnsa2_compliant(alg: str) -> bool:
    """Returns True if the algorithm complies with US CNSA 2.0 requirements."""
    return alg in CNSA2_COMPLIANT_ALGORITHMS


def is_algorithm_permitted(alg: str, profile: str = PqcProfile.HYBRID_V1) -> bool:
    """Validates whether an algorithm is allowed under the chosen PQC security profile.

    In 'pqc-strict' mode, classical algorithms (ES256, RSA) are strictly rejected.
    """
    if profile == PqcProfile.PQC_STRICT or str(profile).lower() == "pqc-strict":
        return is_post_quantum(alg)
    return alg == PqcAlgorithm.ES256 or is_post_quantum(alg)


@dataclass
class PqcReadinessAudit:
    readiness_score: float
    active_profile: str
    total_keys: int
    pqc_keys: int
    classical_keys: int
    total_licenses: int
    pqc_licenses: int
    at_risk_licenses: int
    is_cnsa2_ready: bool
    is_nis2_ready: bool
    action_items: List[str] = field(default_factory=list)
    summary: str = ""


def calculate_pqc_readiness(
    keys: List[Dict[str, Any]],
    licenses: List[Dict[str, Any]],
    active_profile: str = PqcProfile.HYBRID_V1,
) -> PqcReadinessAudit:
    """Audits cryptographic keys and licenses and computes PQC Readiness Index (0..100%)."""
    total_keys = len(keys)
    pqc_keys = sum(1 for k in keys if is_post_quantum(k.get("alg", "")))
    classical_keys = total_keys - pqc_keys

    total_licenses = len(licenses)
    pqc_licenses = 0
    at_risk_licenses = 0

    now = datetime.now(timezone.utc)
    for lic in licenses:
        alg = lic.get("alg", PqcAlgorithm.ES256)
        if is_post_quantum(alg):
            pqc_licenses += 1
        else:
            at_risk_licenses += 1

    key_score = (pqc_keys / total_keys * 60.0) if total_keys > 0 else 30.0
    lic_score = (pqc_licenses / total_licenses * 30.0) if total_licenses > 0 else 15.0
    prof_score = 10.0 if active_profile in (PqcProfile.PQC_STRICT, "pqc-strict") else 5.0

    total_score = round(max(0.0, min(100.0, key_score + lic_score + prof_score)), 1)
    is_cnsa2 = (
        active_profile in (PqcProfile.PQC_STRICT, "pqc-strict")
        and classical_keys == 0
        and all(is_cnsa2_compliant(k.get("alg", "")) for k in keys)
    )
    is_nis2 = total_score >= 70.0

    actions = []
    if classical_keys > 0:
        actions.append(f"Rotate {classical_keys} classical keys to ML-DSA-65 or ML-KEM-768.")
    if atRiskLicenses := at_risk_licenses:
        actions.append(f"Re-sign {atRiskLicenses} perpetual or long-lived licenses with ML-DSA-65.")
    if active_profile not in (PqcProfile.PQC_STRICT, "pqc-strict"):
        actions.append("Upgrade server profile to 'pqc-strict' (Zero Classical Cryptography).")

    summary = (
        "EXCELLENT: Fully ready for Post-Quantum Era (FIPS 203 & 204)."
        if total_score >= 95.0
        else "GOOD: Hybrid profile active. Complete migration to pure PQC."
        if total_score >= 70.0
        else "PARTIAL: Detected vulnerable classical components."
        if total_score >= 40.0
        else "CRITICAL: Infrastructure relies on classical cryptography vulnerable to Q-Day."
    )

    return PqcReadinessAudit(
        readiness_score=total_score,
        active_profile=str(active_profile),
        total_keys=total_keys,
        pqc_keys=pqc_keys,
        classical_keys=classical_keys,
        total_licenses=total_licenses,
        pqc_licenses=pqc_licenses,
        at_risk_licenses=at_risk_licenses,
        is_cnsa2_ready=is_cnsa2,
        is_nis2_ready=is_nis2,
        action_items=actions,
        summary=summary,
    )
