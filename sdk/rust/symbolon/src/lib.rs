//! Official Rust client SDK for the Symbolon Floating & Enterprise License Server.

pub mod agent_ipc;
pub mod attestation;
pub mod client;
pub mod experiment;
pub mod fingerprint;
pub mod models;
pub mod pqc;
pub mod validator;

pub use agent_ipc::{AgentActionResponse, AgentIpcClient, AgentStatus};
pub use attestation::{EnclaveType, HardwareAttestationQuote, TpmQuoteGenerator, TpmQuoteVerifier};
pub use client::{FeatureLease, SeatLease, SymbolonClient, TokenReservationScope};
pub use validator::ParsedToken;
pub use experiment::{
    calculate_bucket, calculate_two_proportion_z_test, calculate_welch_t_test,
    generate_statistical_report, route_experiment, Experiment, ExperimentCircuitBreaker,
    ExperimentEvaluationResult, ExperimentOverrides, ExperimentStatus, ExperimentTargeting,
    ExperimentVariant, StatisticalReport, VariantMetrics, ZTestResult,
};
pub use fingerprint::{compute_canonical_fingerprint, get_local_fingerprint};
pub use models::{
    ActiveFeatureInfo, CommitTokensRequest, CommitTokensResponse, FeatureAcquisitionResponse,
    HeartbeatTokensRequest, HeartbeatTokensResponse, LeaseToken, ReleaseFeatureResponse,
    ReserveTokensRequest, ReserveTokensResponse, RollbackTokensRequest, RollbackTokensResponse,
    SymbolonError, TokenWalletBalance,
};
pub use pqc::{
    calculate_pqc_readiness, is_algorithm_permitted, is_cnsa2_compliant, is_post_quantum,
    KeyAuditInfo, LicenseAuditInfo, PqcAlgorithm, PqcProfile, PqcReadinessAudit,
    CNSA2_COMPLIANT_ALGORITHMS, POST_QUANTUM_ALGORITHMS,
};
