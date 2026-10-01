//! Symbolon Post-Quantum Era Suite for Rust SDK (Phase 25 / Milestone M7, §13.5).
//!
//! Provides PQC profile enforcement, algorithm definitions (FIPS 203, FIPS 204, FIPS 205),
//! and quantum vulnerability auditing (CNSA 2.0 / EU NIS 2 compliance).

use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Serialize, Deserialize)]
#[serde(rename_all = "kebab-case")]
pub enum PqcProfile {
    #[serde(rename = "hybrid-v1")]
    HybridV1,
    #[serde(rename = "pqc-strict")]
    PqcStrict,
}

pub struct PqcAlgorithm;

impl PqcAlgorithm {
    // Classical
    pub const ES256: &'static str = "ES256";

    // FIPS 204: ML-DSA (Module-Lattice Digital Signatures)
    pub const ML_DSA_44: &'static str = "ML-DSA-44";
    pub const ML_DSA_65: &'static str = "ML-DSA-65";
    pub const ML_DSA_87: &'static str = "ML-DSA-87";

    // FIPS 203: ML-KEM (Module-Lattice Key Encapsulation)
    pub const ML_KEM_512: &'static str = "ML-KEM-512";
    pub const ML_KEM_768: &'static str = "ML-KEM-768";
    pub const ML_KEM_1024: &'static str = "ML-KEM-1024";

    // FIPS 205: SLH-DSA (Stateless Hash-Based Signatures / SPHINCS+)
    pub const SLH_DSA_SHA2_128S: &'static str = "SLH-DSA-SHA2-128s";
    pub const SLH_DSA_SHA2_128F: &'static str = "SLH-DSA-SHA2-128f";
    pub const SLH_DSA_SHAKE_128S: &'static str = "SLH-DSA-SHAKE-128s";
}

pub const POST_QUANTUM_ALGORITHMS: &[&str] = &[
    PqcAlgorithm::ML_DSA_44,
    PqcAlgorithm::ML_DSA_65,
    PqcAlgorithm::ML_DSA_87,
    PqcAlgorithm::ML_KEM_512,
    PqcAlgorithm::ML_KEM_768,
    PqcAlgorithm::ML_KEM_1024,
    PqcAlgorithm::SLH_DSA_SHA2_128S,
    PqcAlgorithm::SLH_DSA_SHA2_128F,
    PqcAlgorithm::SLH_DSA_SHAKE_128S,
];

pub const CNSA2_COMPLIANT_ALGORITHMS: &[&str] = &[
    PqcAlgorithm::ML_DSA_65,
    PqcAlgorithm::ML_DSA_87,
    PqcAlgorithm::ML_KEM_768,
    PqcAlgorithm::ML_KEM_1024,
    PqcAlgorithm::SLH_DSA_SHA2_128S,
    PqcAlgorithm::SLH_DSA_SHAKE_128S,
];

/// Returns true if the algorithm is quantum-safe according to NIST PQC standards.
pub fn is_post_quantum(alg: &str) -> bool {
    POST_QUANTUM_ALGORITHMS.iter().any(|&a| a.eq_ignore_ascii_case(alg))
}

/// Returns true if the algorithm complies with US CNSA 2.0 requirements.
pub fn is_cnsa2_compliant(alg: &str) -> bool {
    CNSA2_COMPLIANT_ALGORITHMS.iter().any(|&a| a.eq_ignore_ascii_case(alg))
}

/// Validates whether an algorithm is allowed under the chosen PQC security profile.
/// In 'pqc-strict' mode, classical algorithms (ES256, RSA) are strictly rejected.
pub fn is_algorithm_permitted(alg: &str, profile: PqcProfile) -> bool {
    match profile {
        PqcProfile::PqcStrict => is_post_quantum(alg),
        PqcProfile::HybridV1 => alg.eq_ignore_ascii_case(PqcAlgorithm::ES256) || is_post_quantum(alg),
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct KeyAuditInfo {
    #[serde(rename = "keyId")]
    pub key_id: String,
    pub alg: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct LicenseAuditInfo {
    #[serde(rename = "licenseId")]
    pub license_id: String,
    pub alg: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct PqcReadinessAudit {
    #[serde(rename = "readinessScore")]
    pub readiness_score: f64,
    #[serde(rename = "activeProfile")]
    pub active_profile: PqcProfile,
    #[serde(rename = "totalKeys")]
    pub total_keys: usize,
    #[serde(rename = "pqcKeys")]
    pub pqc_keys: usize,
    #[serde(rename = "classicalKeys")]
    pub classical_keys: usize,
    #[serde(rename = "totalLicenses")]
    pub total_licenses: usize,
    #[serde(rename = "pqcLicenses")]
    pub pqc_licenses: usize,
    #[serde(rename = "atRiskLicenses")]
    pub at_risk_licenses: usize,
    #[serde(rename = "isCnsa2Ready")]
    pub is_cnsa2_ready: bool,
    #[serde(rename = "isNis2Ready")]
    pub is_nis2_ready: bool,
    #[serde(rename = "actionItems")]
    pub action_items: Vec<String>,
    pub summary: String,
}

/// Audits cryptographic keys and licenses and computes PQC Readiness Index (0..100%).
pub fn calculate_pqc_readiness(
    keys: &[KeyAuditInfo],
    licenses: &[LicenseAuditInfo],
    active_profile: PqcProfile,
) -> PqcReadinessAudit {
    let total_keys = keys.len();
    let pqc_keys = keys.iter().filter(|k| is_post_quantum(&k.alg)).count();
    let classical_keys = total_keys.saturating_sub(pqc_keys);

    let total_licenses = licenses.len();
    let mut pqc_licenses = 0;
    let mut at_risk_licenses = 0;

    for lic in licenses {
        let alg = lic.alg.as_deref().unwrap_or(PqcAlgorithm::ES256);
        if is_post_quantum(alg) {
            pqc_licenses += 1;
        } else {
            at_risk_licenses += 1;
        }
    }

    let key_score = if total_keys > 0 {
        (pqc_keys as f64 / total_keys as f64) * 60.0
    } else {
        30.0
    };

    let lic_score = if total_licenses > 0 {
        (pqc_licenses as f64 / total_licenses as f64) * 30.0
    } else {
        15.0
    };

    let prof_score = match active_profile {
        PqcProfile::PqcStrict => 10.0,
        PqcProfile::HybridV1 => 5.0,
    };

    let total_score = ((key_score + lic_score + prof_score).max(0.0).min(100.0) * 10.0).round() / 10.0;

    let is_cnsa2 = active_profile == PqcProfile::PqcStrict
        && classical_keys == 0
        && !keys.is_empty()
        && keys.iter().all(|k| is_cnsa2_compliant(&k.alg));

    let is_nis2 = total_score >= 70.0;

    let mut actions = Vec::new();
    if classical_keys > 0 {
        actions.push(format!("Rotate {classical_keys} classical keys to ML-DSA-65 or ML-KEM-768."));
    }
    if at_risk_licenses > 0 {
        actions.push(format!("Re-sign {at_risk_licenses} perpetual or long-lived licenses with ML-DSA-65."));
    }
    if active_profile != PqcProfile::PqcStrict {
        actions.push("Upgrade server profile to 'pqc-strict' (Zero Classical Cryptography).".to_string());
    }

    let summary = if total_score >= 95.0 {
        "EXCELLENT: Fully ready for Post-Quantum Era (FIPS 203 & 204)."
    } else if total_score >= 70.0 {
        "GOOD: Hybrid profile active. Complete migration to pure PQC."
    } else if total_score >= 40.0 {
        "PARTIAL: Detected vulnerable classical components."
    } else {
        "CRITICAL: Infrastructure relies on classical cryptography vulnerable to Q-Day."
    };

    PqcReadinessAudit {
        readiness_score: total_score,
        active_profile,
        total_keys,
        pqc_keys,
        classical_keys,
        total_licenses,
        pqc_licenses,
        at_risk_licenses,
        is_cnsa2_ready: is_cnsa2,
        is_nis2_ready: is_nis2,
        action_items: actions,
        summary: summary.to_string(),
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_algorithm_classification() {
        assert!(is_post_quantum(PqcAlgorithm::ML_DSA_65));
        assert!(is_post_quantum(PqcAlgorithm::ML_KEM_768));
        assert!(is_post_quantum(PqcAlgorithm::SLH_DSA_SHA2_128S));
        assert!(!is_post_quantum("ES256"));
        assert!(!is_post_quantum("RSA-2048"));

        assert!(is_cnsa2_compliant(PqcAlgorithm::ML_DSA_65));
        assert!(is_cnsa2_compliant(PqcAlgorithm::ML_DSA_87));
        assert!(!is_cnsa2_compliant(PqcAlgorithm::ML_DSA_44));
    }

    #[test]
    fn test_profile_enforcement() {
        assert!(is_algorithm_permitted(PqcAlgorithm::ES256, PqcProfile::HybridV1));
        assert!(is_algorithm_permitted(PqcAlgorithm::ML_DSA_65, PqcProfile::HybridV1));

        assert!(!is_algorithm_permitted(PqcAlgorithm::ES256, PqcProfile::PqcStrict));
        assert!(is_algorithm_permitted(PqcAlgorithm::ML_DSA_65, PqcProfile::PqcStrict));
    }

    #[test]
    fn test_audit_calculation() {
        let keys = vec![
            KeyAuditInfo { key_id: "k1".to_string(), alg: "ML-DSA-65".to_string() },
            KeyAuditInfo { key_id: "k2".to_string(), alg: "ML-KEM-768".to_string() },
        ];
        let licenses = vec![
            LicenseAuditInfo { license_id: "lic1".to_string(), alg: Some("ML-DSA-65".to_string()) },
        ];

        let audit = calculate_pqc_readiness(&keys, &licenses, PqcProfile::PqcStrict);
        assert_eq!(audit.readiness_score, 100.0);
        assert!(audit.is_cnsa2_ready);
        assert!(audit.is_nis2_ready);
        assert_eq!(audit.pqc_keys, 2);
        assert_eq!(audit.classical_keys, 0);
        assert_eq!(audit.pqc_licenses, 1);
        assert_eq!(audit.at_risk_licenses, 0);
    }
}
