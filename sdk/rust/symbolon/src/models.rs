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
