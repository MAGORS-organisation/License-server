//! Symbolon A/B Testing & Experimentation Engine for Rust SDK (Phase 24 / AB-1..15).
//!
//! Provides deterministic bucketing, experiment routing with sticky session guarantees,
//! and statistical analysis tools (Z-Test, Welch's t-test, confidence intervals).

use crate::models::SymbolonError;
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::collections::HashMap;

#[derive(Debug, Clone, Copy, PartialEq, Eq, Hash, Serialize, Deserialize)]
#[serde(rename_all = "PascalCase")]
pub enum ExperimentStatus {
    Draft,
    Active,
    Paused,
    Completed,
    RolledBack,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct ExperimentOverrides {
    #[serde(rename = "leaseTtlSeconds", skip_serializing_if = "Option::is_none")]
    pub lease_ttl_seconds: Option<i64>,
    #[serde(rename = "policyRulesYaml", skip_serializing_if = "Option::is_none")]
    pub policy_rules_yaml: Option<String>,
    #[serde(rename = "featureFlags", default)]
    pub feature_flags: HashMap<String, bool>,
    #[serde(rename = "customMetadata", default)]
    pub custom_metadata: HashMap<String, String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ExperimentVariant {
    #[serde(rename = "variantId")]
    pub variant_id: String,
    pub name: String,
    pub weight: i32,
    #[serde(rename = "isControl", default)]
    pub is_control: bool,
    #[serde(default)]
    pub overrides: ExperimentOverrides,
}

#[derive(Debug, Clone, Default, Serialize, Deserialize)]
pub struct ExperimentTargeting {
    #[serde(rename = "targetTenants", default)]
    pub target_tenants: Vec<String>,
    #[serde(rename = "licenseKeyPrefixes", default)]
    pub license_key_prefixes: Vec<String>,
    #[serde(rename = "allowedSdkVersions", default)]
    pub allowed_sdk_versions: Vec<String>,
    #[serde(rename = "operatingSystems", default)]
    pub operating_systems: Vec<String>,
}

impl ExperimentTargeting {
    pub fn matches(
        &self,
        tenant_id: Option<&str>,
        license_key: &str,
        context: Option<&HashMap<String, String>>,
    ) -> bool {
        if !self.target_tenants.is_empty() {
            match tenant_id {
                Some(tid) if self.target_tenants.iter().any(|t| t == tid) => {}
                _ => return false,
            }
        }

        if !self.license_key_prefixes.is_empty() {
            let key_upper = license_key.to_uppercase();
            if !self.license_key_prefixes.iter().any(|p| key_upper.starts_with(&p.to_uppercase())) {
                return false;
            }
        }

        if let Some(ctx) = context {
            if !self.allowed_sdk_versions.is_empty() {
                let sdk = ctx.get("sdk_version").or_else(|| ctx.get("sdkVersion"));
                match sdk {
                    Some(s) if self.allowed_sdk_versions.iter().any(|v| v == s) => {}
                    _ => return false,
                }
            }

            if !self.operating_systems.is_empty() {
                let os_name = ctx.get("os_platform").or_else(|| ctx.get("osPlatform")).or_else(|| ctx.get("os"));
                match os_name {
                    Some(os) => {
                        let os_lower = os.to_lowercase();
                        if !self.operating_systems.iter().any(|o| os_lower.contains(&o.to_lowercase())) {
                            return false;
                        }
                    }
                    None => return false,
                }
            }
        }

        true
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ExperimentCircuitBreaker {
    #[serde(rename = "maxErrorRate", default = "default_max_error_rate")]
    pub max_error_rate: f64,
    #[serde(rename = "minSamples", default = "default_min_samples")]
    pub min_samples: i32,
    #[serde(rename = "autoRollback", default = "default_auto_rollback")]
    pub auto_rollback: bool,
}

fn default_max_error_rate() -> f64 { 0.05 }
fn default_min_samples() -> i32 { 50 }
fn default_auto_rollback() -> bool { true }

impl Default for ExperimentCircuitBreaker {
    fn default() -> Self {
        Self {
            max_error_rate: 0.05,
            min_samples: 50,
            auto_rollback: true,
        }
    }
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct Experiment {
    pub id: String,
    pub name: String,
    #[serde(default)]
    pub description: String,
    #[serde(default = "default_status")]
    pub status: ExperimentStatus,
    #[serde(rename = "trafficAllocation", default = "default_traffic_allocation")]
    pub traffic_allocation: i32,
    #[serde(default)]
    pub salt: String,
    #[serde(rename = "promotedVariantId", skip_serializing_if = "Option::is_none")]
    pub promoted_variant_id: Option<String>,
    #[serde(default)]
    pub targeting: ExperimentTargeting,
    #[serde(default)]
    pub variants: Vec<ExperimentVariant>,
    #[serde(rename = "circuitBreaker", default)]
    pub circuit_breaker: ExperimentCircuitBreaker,
}

fn default_status() -> ExperimentStatus { ExperimentStatus::Draft }
fn default_traffic_allocation() -> i32 { 100 }

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ExperimentEvaluationResult {
    #[serde(rename = "experimentId")]
    pub experiment_id: String,
    #[serde(rename = "variantId")]
    pub variant_id: String,
    #[serde(rename = "isInExperiment")]
    pub is_in_experiment: bool,
    #[serde(rename = "isControl")]
    pub is_control: bool,
    #[serde(default)]
    pub overrides: ExperimentOverrides,
}

impl ExperimentEvaluationResult {
    pub fn not_in_experiment(experiment_id: impl Into<String>) -> Self {
        Self {
            experiment_id: experiment_id.into(),
            variant_id: "baseline".to_string(),
            is_in_experiment: false,
            is_control: true,
            overrides: ExperimentOverrides::default(),
        }
    }
}

/// Calculates a deterministic, stateless bucket [0..99] for a given license key,
/// machine ID, and experiment salt using SHA-256 little-endian uint32 modulo 100.
pub fn calculate_bucket(license_key: &str, machine_id: &str, salt: &str) -> Result<u32, SymbolonError> {
    if license_key.trim().is_empty() {
        return Err(SymbolonError::InvalidArgument("license_key cannot be empty".to_string()));
    }
    if machine_id.trim().is_empty() {
        return Err(SymbolonError::InvalidArgument("machine_id cannot be empty".to_string()));
    }

    let input = format!("{}:{}:{}", license_key.trim().to_uppercase(), machine_id.trim(), salt);
    let mut hasher = Sha256::new();
    hasher.update(input.as_bytes());
    let hash = hasher.finalize();

    let val = u32::from_le_bytes(hash[..4].try_into().unwrap());
    Ok(val % 100)
}

/// Evaluates an experiment and assigns client to an experiment variant or baseline.
/// Guarantees sticky session invariant: zero variant drift for fixed (license_key, machine_id, salt).
pub fn route_experiment(
    experiment: &Experiment,
    tenant_id: Option<&str>,
    license_key: &str,
    machine_id: &str,
    context: Option<&HashMap<String, String>>,
) -> Result<ExperimentEvaluationResult, SymbolonError> {
    if license_key.trim().is_empty() {
        return Err(SymbolonError::InvalidArgument("license_key cannot be empty".to_string()));
    }
    if machine_id.trim().is_empty() {
        return Err(SymbolonError::InvalidArgument("machine_id cannot be empty".to_string()));
    }

    // If completed and promoted, always route to promoted variant
    if experiment.status == ExperimentStatus::Completed {
        if let Some(ref promoted_id) = experiment.promoted_variant_id {
            if let Some(v) = experiment.variants.iter().find(|v| &v.variant_id == promoted_id) {
                return Ok(ExperimentEvaluationResult {
                    experiment_id: experiment.id.clone(),
                    variant_id: v.variant_id.clone(),
                    is_in_experiment: true,
                    is_control: v.is_control,
                    overrides: v.overrides.clone(),
                });
            }
        }
    }

    if experiment.status != ExperimentStatus::Active {
        return Ok(ExperimentEvaluationResult::not_in_experiment(&experiment.id));
    }

    if !experiment.targeting.matches(tenant_id, license_key, context) {
        return Ok(ExperimentEvaluationResult::not_in_experiment(&experiment.id));
    }

    let bucket = calculate_bucket(license_key, machine_id, &experiment.salt)? as i32;
    if bucket >= experiment.traffic_allocation || experiment.traffic_allocation <= 0 {
        return Ok(ExperimentEvaluationResult::not_in_experiment(&experiment.id));
    }

    if experiment.variants.is_empty() {
        return Ok(ExperimentEvaluationResult::not_in_experiment(&experiment.id));
    }

    let total_weight: i32 = experiment.variants.iter().map(|v| v.weight.max(0)).sum();
    if total_weight <= 0 {
        return Ok(ExperimentEvaluationResult::not_in_experiment(&experiment.id));
    }

    let scaled_point = (bucket * total_weight) / experiment.traffic_allocation;

    let mut accumulated = 0;
    for v in &experiment.variants {
        accumulated += v.weight.max(0);
        if scaled_point < accumulated {
            return Ok(ExperimentEvaluationResult {
                experiment_id: experiment.id.clone(),
                variant_id: v.variant_id.clone(),
                is_in_experiment: true,
                is_control: v.is_control,
                overrides: v.overrides.clone(),
            });
        }
    }

    let fallback = experiment.variants.last().unwrap();
    Ok(ExperimentEvaluationResult {
        experiment_id: experiment.id.clone(),
        variant_id: fallback.variant_id.clone(),
        is_in_experiment: true,
        is_control: fallback.is_control,
        overrides: fallback.overrides.clone(),
    })
}

// Statistical Engine
pub const Z95: f64 = 1.959963984540054;

/// Computes error function erf(x) using Abramowitz and Stegun 7.1.26 polynomial approximation (max error 1.5e-7).
pub fn erf(x: f64) -> f64 {
    let sign = if x < 0.0 { -1.0 } else { 1.0 };
    let x = x.abs();

    let a1 = 0.254829592;
    let a2 = -0.284496736;
    let a3 = 1.421413741;
    let a4 = -1.453152027;
    let a5 = 1.061405429;
    let p = 0.3275911;

    let t = 1.0 / (1.0 + p * x);
    let y = 1.0 - (((((a5 * t + a4) * t) + a3) * t + a2) * t + a1) * t * (-x * x).exp();
    sign * y
}

/// Cumulative distribution function for standard normal distribution N(0,1).
pub fn normal_cdf(z: f64) -> f64 {
    0.5 * (1.0 + erf(z / std::f64::consts::SQRT_2))
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ZTestResult {
    pub z_score: f64,
    pub p_value: f64,
    pub ci_lower: f64,
    pub ci_upper: f64,
}

pub fn calculate_two_proportion_z_test(
    successes_a: u64,
    trials_a: u64,
    successes_b: u64,
    trials_b: u64,
) -> ZTestResult {
    if trials_a == 0 || trials_b == 0 {
        return ZTestResult {
            z_score: 0.0,
            p_value: 1.0,
            ci_lower: 0.0,
            ci_upper: 0.0,
        };
    }

    let p_a = successes_a as f64 / trials_a as f64;
    let p_b = successes_b as f64 / trials_b as f64;
    let diff = p_b - p_a;

    let pooled_p = (successes_a + successes_b) as f64 / (trials_a + trials_b) as f64;
    let pooled_var = pooled_p * (1.0 - pooled_p) * ((1.0 / trials_a as f64) + (1.0 / trials_b as f64));

    let z_score = if pooled_var > 0.0 {
        diff / pooled_var.sqrt()
    } else {
        0.0
    };

    let p_val = (2.0 * (1.0 - normal_cdf(z_score.abs()))).clamp(0.0, 1.0);

    let se_diff = ((p_a * (1.0 - p_a) / trials_a as f64) + (p_b * (1.0 - p_b) / trials_b as f64)).sqrt();
    let ci_lower = diff - (Z95 * se_diff);
    let ci_upper = diff + (Z95 * se_diff);

    ZTestResult {
        z_score,
        p_value: p_val,
        ci_lower,
        ci_upper,
    }
}

pub fn calculate_welch_t_test(
    mean_a: f64,
    std_dev_a: f64,
    n_a: u64,
    mean_b: f64,
    std_dev_b: f64,
    n_b: u64,
) -> (f64, f64) {
    if n_a <= 1 || n_b <= 1 {
        return (0.0, 1.0);
    }

    let var_a = std_dev_a * std_dev_a;
    let var_b = std_dev_b * std_dev_b;
    let denom = ((var_a / n_a as f64) + (var_b / n_b as f64)).sqrt();

    if denom <= 0.0 {
        return (0.0, 1.0);
    }

    let t_score = (mean_b - mean_a) / denom;
    let p_val = (2.0 * (1.0 - normal_cdf(t_score.abs()))).clamp(0.0, 1.0);

    (t_score, p_val)
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct VariantMetrics {
    pub total_requests: u64,
    pub successful_checkouts: u64,
    pub average_latency_ms: f64,
    pub latency_std_dev: f64,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct StatisticalReport {
    pub experiment_id: String,
    pub z_score: f64,
    pub p_value: f64,
    pub confidence_interval_lower: f64,
    pub confidence_interval_upper: f64,
    pub is_statistically_significant: bool,
    pub difference_rate: f64,
    pub latency_t_score: f64,
    pub latency_p_value: f64,
    pub latency_difference_ms: f64,
    pub recommendation: String,
}

pub fn generate_statistical_report(
    experiment: &Experiment,
    control_metrics: &VariantMetrics,
    treatment_metrics: &VariantMetrics,
) -> StatisticalReport {
    let trials_a = control_metrics.total_requests;
    let succ_a = control_metrics.successful_checkouts;
    let lat_a = control_metrics.average_latency_ms;
    let std_a = control_metrics.latency_std_dev;

    let trials_b = treatment_metrics.total_requests;
    let succ_b = treatment_metrics.successful_checkouts;
    let lat_b = treatment_metrics.average_latency_ms;
    let std_b = treatment_metrics.latency_std_dev;

    let z_res = calculate_two_proportion_z_test(succ_a, trials_a, succ_b, trials_b);
    let (t_score, t_p_val) = calculate_welch_t_test(lat_a, std_a, trials_a, lat_b, std_b, trials_b);

    let rate_a = if trials_a > 0 { succ_a as f64 / trials_a as f64 } else { 0.0 };
    let rate_b = if trials_b > 0 { succ_b as f64 / trials_b as f64 } else { 0.0 };
    let diff = rate_b - rate_a;
    let lat_diff = lat_b - lat_a;

    let is_significant = (z_res.p_value < 0.05) && (trials_a >= 30 && trials_b >= 30);

    let err_rate_a = if trials_a > 0 { (trials_a.saturating_sub(succ_a)) as f64 / trials_a as f64 } else { 0.0 };
    let err_rate_b = if trials_b > 0 { (trials_b.saturating_sub(succ_b)) as f64 / trials_b as f64 } else { 0.0 };

    let recommendation = if trials_a < 30 || trials_b < 30 {
        format!("Nedostatok vzoriek (Control: {trials_a}, Treatment: {trials_b}). Minimum je 30 požiadaviek na variant.")
    } else if err_rate_b > err_rate_a && (err_rate_b - err_rate_a) > 0.03 {
        format!("VAROVANIE: Variant 'Treatment' vykazuje zvýšenú chybovosť ({:.1}% vs. {:.1}%). Odporúča sa rollback.", err_rate_b * 100.0, err_rate_a * 100.0)
    } else if is_significant && diff > 0.0 {
        format!("ODPORÚČANIE: Variant 'Treatment' vykazuje štatisticky signifikantné zlepšenie úspešnosti (+{:.1}%, p={:.4}). Bezpečné nasadiť na 100%.", diff * 100.0, z_res.p_value)
    } else if t_p_val < 0.05 && lat_diff < -5.0 {
        format!("ODPORÚČANIE: Variant 'Treatment' signifikantne znižuje latenciu ({:.1} ms, p={:.4}).", lat_diff, t_p_val)
    } else if is_significant && diff < 0.0 {
        format!("VAROVANIE: Variant 'Treatment' vykazuje signifikantný pokles úspešnosti ({:.1}%, p={:.4}).", diff * 100.0, z_res.p_value)
    } else {
        format!("Medzi variantmi zatiaľ nebol preukázaný štatisticky signifikantný rozdiel (p = {:.4}). Pokračujte v zbere dát.", z_res.p_value)
    };

    StatisticalReport {
        experiment_id: experiment.id.clone(),
        z_score: z_res.z_score,
        p_value: z_res.p_value,
        confidence_interval_lower: z_res.ci_lower,
        confidence_interval_upper: z_res.ci_upper,
        is_statistically_significant: is_significant,
        difference_rate: diff,
        latency_t_score: t_score,
        latency_p_value: t_p_val,
        latency_difference_ms: lat_diff,
        recommendation,
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_calculate_bucket_deterministic() {
        let b1 = calculate_bucket("SYM-ABCD-1234", "mach-1", "saltA").unwrap();
        let b2 = calculate_bucket("SYM-ABCD-1234", "mach-1", "saltA").unwrap();
        assert_eq!(b1, b2);
        assert!(b1 < 100);

        let b3 = calculate_bucket("sym-abcd-1234", "mach-1", "saltA").unwrap();
        assert_eq!(b1, b3); // Case insensitive for license key
    }

    #[test]
    fn test_route_experiment_control_treatment() {
        let exp = Experiment {
            id: "exp_opt".to_string(),
            name: "Optimization Exp".to_string(),
            description: String::new(),
            status: ExperimentStatus::Active,
            traffic_allocation: 100,
            salt: "salt123".to_string(),
            promoted_variant_id: None,
            targeting: ExperimentTargeting::default(),
            variants: vec![
                ExperimentVariant {
                    variant_id: "ctrl".to_string(),
                    name: "Control".to_string(),
                    weight: 50,
                    is_control: true,
                    overrides: ExperimentOverrides::default(),
                },
                ExperimentVariant {
                    variant_id: "treat".to_string(),
                    name: "Treatment".to_string(),
                    weight: 50,
                    is_control: false,
                    overrides: ExperimentOverrides::default(),
                },
            ],
            circuit_breaker: ExperimentCircuitBreaker::default(),
        };

        let res = route_experiment(&exp, None, "SYM-KEY-1", "mach-1", None).unwrap();
        assert!(res.is_in_experiment);
        assert!(res.variant_id == "ctrl" || res.variant_id == "treat");
    }

    #[test]
    fn test_z_test_calculation() {
        let res = calculate_two_proportion_z_test(90, 100, 95, 100);
        assert!(res.z_score > 0.0);
        assert!(res.p_value <= 1.0);
        assert!(res.ci_lower < res.ci_upper);
    }
}
