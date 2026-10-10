use serde::{Deserialize, Serialize};
use std::fmt;

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct LeaseToken {
    #[serde(rename = "leaseId")]
    pub lease_id: String,
    pub token: String,
    pub seat: i32,
    #[serde(rename = "expiresAt")]
    pub expires_at: String,
    pub entitlements: Vec<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct FeatureAcquisitionResponse {
    pub success: bool,
    #[serde(rename = "featureCode")]
    pub feature_code: String,
    #[serde(rename = "inUse")]
    pub in_use: i32,
    #[serde(rename = "maxSeats")]
    pub max_seats: i32,
    #[serde(rename = "expiresAt")]
    pub expires_at: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ActiveFeatureInfo {
    #[serde(rename = "featureCode")]
    pub feature_code: String,
    pub version: Option<String>,
    #[serde(rename = "expiresAt")]
    pub expires_at: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ReleaseFeatureResponse {
    pub success: bool,
    #[serde(rename = "featureCode")]
    pub feature_code: String,
}

#[derive(Debug, Clone)]
pub enum SymbolonError {
    InvalidArgument(String),
    CapacityExhausted,
    LicenseNotFound,
    FeatureDenied(String),
    FeatureCapacityExceeded(String),
    Network(String),
    Serialization(String),
}

impl fmt::Display for SymbolonError {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            SymbolonError::InvalidArgument(s) => write!(f, "Invalid argument: {s}"),
            SymbolonError::CapacityExhausted => write!(f, "Floating pool capacity exhausted (0 seats available)"),
            SymbolonError::LicenseNotFound => write!(f, "License not found or inactive"),
            SymbolonError::FeatureDenied(s) => write!(f, "Feature denied: {s}"),
            SymbolonError::FeatureCapacityExceeded(s) => write!(f, "Feature capacity exceeded: {s}"),
            SymbolonError::Network(s) => write!(f, "Network error: {s}"),
            SymbolonError::Serialization(s) => write!(f, "Serialization error: {s}"),
        }
    }
}

impl std::error::Error for SymbolonError {}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ReserveTokensRequest {
    #[serde(rename = "walletId")]
    pub wallet_id: String,
    #[serde(rename = "featureCode")]
    pub feature_code: String,
    #[serde(rename = "estimatedUnits")]
    pub estimated_units: f64,
    #[serde(rename = "isDurationMinutes", skip_serializing_if = "Option::is_none")]
    pub is_duration_minutes: Option<bool>,
    #[serde(rename = "reservationTtl", skip_serializing_if = "Option::is_none")]
    pub reservation_ttl: Option<String>,
    #[serde(rename = "clientRef", skip_serializing_if = "Option::is_none")]
    pub client_ref: Option<String>,
    #[serde(rename = "machineId", skip_serializing_if = "Option::is_none")]
    pub machine_id: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct ReserveTokensResponse {
    pub success: bool,
    #[serde(rename = "reservationId")]
    pub reservation_id: Option<String>,
    #[serde(rename = "reservedAmount", default)]
    pub reserved_amount: f64,
    #[serde(rename = "availableBalance", default)]
    pub available_balance: f64,
    #[serde(rename = "overdraftRemaining", default)]
    pub overdraft_remaining: f64,
    #[serde(rename = "failureReason")]
    pub failure_reason: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct HeartbeatTokensRequest {
    #[serde(rename = "reservationId")]
    pub reservation_id: String,
    #[serde(rename = "deltaUnits")]
    pub delta_units: f64,
    #[serde(rename = "isDurationMinutes", default)]
    pub is_duration_minutes: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct HeartbeatTokensResponse {
    pub success: bool,
    #[serde(rename = "totalConsumed", default)]
    pub total_consumed: f64,
    #[serde(rename = "remainingReserved", default)]
    pub remaining_reserved: f64,
    #[serde(rename = "availableBalance", default)]
    pub available_balance: f64,
    #[serde(rename = "failureReason")]
    pub failure_reason: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct CommitTokensRequest {
    #[serde(rename = "reservationId")]
    pub reservation_id: String,
    #[serde(rename = "actualUnits")]
    pub actual_units: f64,
    #[serde(rename = "isDurationMinutes", default)]
    pub is_duration_minutes: bool,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct CommitTokensResponse {
    pub success: bool,
    #[serde(rename = "consumedCredits", default)]
    pub consumed_credits: f64,
    #[serde(rename = "refundedCredits", default)]
    pub refunded_credits: f64,
    #[serde(rename = "newBalance", default)]
    pub new_balance: f64,
    #[serde(rename = "failureReason")]
    pub failure_reason: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct RollbackTokensRequest {
    #[serde(rename = "reservationId")]
    pub reservation_id: String,
    #[serde(default)]
    pub reason: String,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct RollbackTokensResponse {
    pub success: bool,
    #[serde(rename = "restoredCredits", default)]
    pub restored_credits: f64,
    #[serde(rename = "newBalance", default)]
    pub new_balance: f64,
    #[serde(rename = "failureReason")]
    pub failure_reason: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct TokenWalletBalance {
    #[serde(rename = "walletId")]
    pub wallet_id: String,
    #[serde(rename = "walletCode")]
    pub wallet_code: String,
    #[serde(rename = "walletName")]
    pub wallet_name: String,
    #[serde(rename = "totalCredits", default)]
    pub total_credits: f64,
    #[serde(default)]
    pub balance: f64,
    #[serde(rename = "reservedCredits", default)]
    pub reserved_credits: f64,
    #[serde(rename = "availableBalance", default)]
    pub available_balance: f64,
    #[serde(rename = "overdraftLimit", default)]
    pub overdraft_limit: f64,
    pub state: String,
    #[serde(rename = "isLowBalance", default)]
    pub is_low_balance: bool,
    #[serde(rename = "expiresAt")]
    pub expires_at: Option<String>,
}
