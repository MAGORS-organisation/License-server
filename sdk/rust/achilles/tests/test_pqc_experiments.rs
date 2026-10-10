use symbolon_client::experiment::{
    calculate_bucket, calculate_two_proportion_z_test, calculate_welch_t_test,
    generate_statistical_report, route_experiment, Experiment, ExperimentCircuitBreaker,
    ExperimentOverrides, ExperimentStatus, ExperimentTargeting, ExperimentVariant, VariantMetrics,
};
use symbolon_client::pqc::{
    calculate_pqc_readiness, is_algorithm_permitted, is_cnsa2_compliant, is_post_quantum,
    KeyAuditInfo, LicenseAuditInfo, PqcAlgorithm, PqcProfile,
};
use std::collections::HashMap;

#[test]
fn test_pqc_algorithms() {
    assert!(is_post_quantum(PqcAlgorithm::ML_DSA_65));
    assert!(is_post_quantum(PqcAlgorithm::ML_KEM_768));
    assert!(is_post_quantum(PqcAlgorithm::SLH_DSA_SHA2_128S));
    assert!(!is_post_quantum("ES256"));
    assert!(!is_post_quantum("RSA-2048"));

    assert!(is_cnsa2_compliant(PqcAlgorithm::ML_DSA_65));
    assert!(is_cnsa2_compliant(PqcAlgorithm::ML_DSA_87));
    assert!(is_cnsa2_compliant(PqcAlgorithm::ML_KEM_768));
    assert!(is_cnsa2_compliant(PqcAlgorithm::ML_KEM_1024));
    assert!(!is_cnsa2_compliant(PqcAlgorithm::ML_DSA_44));
    assert!(!is_cnsa2_compliant(PqcAlgorithm::ML_KEM_512));
}

#[test]
fn test_pqc_profile_enforcement() {
    // In HybridV1, both ES256 and PQC are permitted
    assert!(is_algorithm_permitted(PqcAlgorithm::ES256, PqcProfile::HybridV1));
    assert!(is_algorithm_permitted(PqcAlgorithm::ML_DSA_65, PqcProfile::HybridV1));

    // In PqcStrict, ES256 is strictly rejected
    assert!(!is_algorithm_permitted(PqcAlgorithm::ES256, PqcProfile::PqcStrict));
    assert!(is_algorithm_permitted(PqcAlgorithm::ML_DSA_65, PqcProfile::PqcStrict));
    assert!(is_algorithm_permitted(PqcAlgorithm::ML_KEM_768, PqcProfile::PqcStrict));
}

#[test]
fn test_pqc_readiness_audit() {
    let keys = vec![
        KeyAuditInfo { key_id: "key-1".to_string(), alg: PqcAlgorithm::ML_DSA_65.to_string() },
        KeyAuditInfo { key_id: "key-2".to_string(), alg: PqcAlgorithm::ML_KEM_768.to_string() },
    ];
    let licenses = vec![
        LicenseAuditInfo { license_id: "lic-1".to_string(), alg: Some(PqcAlgorithm::ML_DSA_65.to_string()) },
        LicenseAuditInfo { license_id: "lic-2".to_string(), alg: Some(PqcAlgorithm::ML_DSA_65.to_string()) },
    ];

    let audit = calculate_pqc_readiness(&keys, &licenses, PqcProfile::PqcStrict);
    assert_eq!(audit.readiness_score, 100.0);
    assert!(audit.is_cnsa2_ready);
    assert!(audit.is_nis2_ready);
    assert_eq!(audit.pqc_keys, 2);
    assert_eq!(audit.classical_keys, 0);
    assert_eq!(audit.pqc_licenses, 2);
    assert_eq!(audit.at_risk_licenses, 0);
    assert!(audit.action_items.is_empty());
}

#[test]
fn test_experiment_deterministic_bucketing() {
    let b1 = calculate_bucket("SYM-ABCD-1234", "mach-1", "saltA").unwrap();
    let b2 = calculate_bucket("SYM-ABCD-1234", "mach-1", "saltA").unwrap();
    assert_eq!(b1, b2);
    assert!(b1 < 100);

    // Case-insensitivity
    let b3 = calculate_bucket("sym-abcd-1234", "mach-1", "saltA").unwrap();
    assert_eq!(b1, b3);

    // Different salt yields different bucket distribution
    let mut different = false;
    for i in 0..10 {
        let key = format!("SYM-KEY-{}", i);
        let b_a = calculate_bucket(&key, "mach", "salt_a").unwrap();
        let b_b = calculate_bucket(&key, "mach", "salt_b").unwrap();
        if b_a != b_b {
            different = true;
            break;
        }
    }
    assert!(different);
}

#[test]
fn test_experiment_routing_and_sticky_session() {
    let exp = Experiment {
        id: "exp_test".to_string(),
        name: "Test Experiment".to_string(),
        description: "Testing sticky variant assignment".to_string(),
        status: ExperimentStatus::Active,
        traffic_allocation: 100,
        salt: "pepper42".to_string(),
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

    // Client 1 route
    let res1 = route_experiment(&exp, None, "SYM-KEY-100", "mach-uuid", None).unwrap();
    assert!(res1.is_in_experiment);

    // Re-evaluating 100 times must guarantee identical variant (Zero-drift sticky session)
    for _ in 0..100 {
        let res2 = route_experiment(&exp, None, "SYM-KEY-100", "mach-uuid", None).unwrap();
        assert_eq!(res1.variant_id, res2.variant_id);
    }
}

#[test]
fn test_experiment_statistical_report() {
    let exp = Experiment {
        id: "exp_stat".to_string(),
        name: "Stat Test".to_string(),
        description: "".to_string(),
        status: ExperimentStatus::Active,
        traffic_allocation: 100,
        salt: "salt".to_string(),
        promoted_variant_id: None,
        targeting: ExperimentTargeting::default(),
        variants: vec![],
        circuit_breaker: ExperimentCircuitBreaker::default(),
    };

    let ctrl = VariantMetrics {
        total_requests: 1000,
        successful_checkouts: 900,
        average_latency_ms: 50.0,
        latency_std_dev: 5.0,
    };

    let treat = VariantMetrics {
        total_requests: 1000,
        successful_checkouts: 960, // Significant improvement: 96% vs 90%
        average_latency_ms: 40.0, // Significant decrease: 40ms vs 50ms
        latency_std_dev: 4.0,
    };

    let report = generate_statistical_report(&exp, &ctrl, &treat);
    assert!(report.is_statistically_significant);
    assert!(report.difference_rate > 0.05);
    assert!(report.p_value < 0.001);
    assert!(report.recommendation.contains("ODPORÚČANIE"));
}

#[test]
fn test_token_metered_scope_commit() {
    let client = symbolon_client::SymbolonClient::new("http://localhost:5000", "rust-test");
    let req = symbolon_client::ReserveTokensRequest {
        wallet_id: "wlt_rust_1".to_string(),
        feature_code: "compute_ai".to_string(),
        estimated_units: 100.0,
        is_duration_minutes: None,
        reservation_ttl: None,
        client_ref: None,
        machine_id: None,
    };

    let scope = client.begin_metered_scope(&req).expect("should create scope");
    assert_eq!(scope.wallet_id, "wlt_rust_1");
    assert!(!scope.is_completed());

    let commit_res = scope.commit(80.0, false).expect("commit should succeed");
    assert!(commit_res.success);
    assert!(scope.is_completed());
    assert_eq!(commit_res.consumed_credits, 80.0);
    assert_eq!(commit_res.refunded_credits, 20.0);
}

#[test]
fn test_token_auto_rollback_on_drop() {
    let client = symbolon_client::SymbolonClient::new("http://localhost:5000", "rust-test");
    let req = symbolon_client::ReserveTokensRequest {
        wallet_id: "wlt_rust_2".to_string(),
        feature_code: "render".to_string(),
        estimated_units: 50.0,
        is_duration_minutes: None,
        reservation_ttl: None,
        client_ref: None,
        machine_id: None,
    };

    {
        let scope = client.begin_metered_scope(&req).expect("should create scope");
        assert!(!scope.is_completed());
        // Dropping scope without commit will trigger rollback in Drop
    }
}
