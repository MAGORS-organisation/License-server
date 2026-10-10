//! IPC and Loopback HTTP Client communicating with local `symbolon-agent` daemon.

use crate::models::SymbolonError;
use serde::{Deserialize, Serialize};

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct AgentStatus {
    pub status: String,
    #[serde(rename = "machineId")]
    pub machine_id: String,
    #[serde(rename = "licenseKey")]
    pub license_key: Option<String>,
    #[serde(rename = "leaseId")]
    pub lease_id: Option<String>,
    #[serde(rename = "seatNo")]
    pub seat_no: Option<i32>,
    #[serde(rename = "offlineAllowed")]
    pub offline_allowed: bool,
    #[serde(rename = "lastError")]
    pub last_error: Option<String>,
    #[serde(rename = "expiresAt")]
    pub expires_at: Option<String>,
}

#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct AgentActionResponse {
    pub success: bool,
    pub message: String,
}

/// Client for communicating with the local symbolon-agent (127.0.0.1:8189).
#[derive(Debug, Clone)]
pub struct AgentIpcClient {
    pub base_url: String,
}

impl Default for AgentIpcClient {
    fn default() -> Self {
        Self::new("http://127.0.0.1:8189")
    }
}

impl AgentIpcClient {
    pub fn new(base_url: impl Into<String>) -> Self {
        Self {
            base_url: base_url.into(),
        }
    }

    /// Helper for formatting endpoint URLs.
    pub fn endpoint(&self, path: &str) -> String {
        format!("{}/{}", self.base_url.trim_end_matches('/'), path.trim_start_matches('/'))
    }
}
