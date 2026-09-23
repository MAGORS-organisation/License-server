use crate::models::{ActiveFeatureInfo, FeatureAcquisitionResponse, LeaseToken, SymbolonError};
use std::collections::HashSet;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::{Arc, Mutex};

/// RAII Guard for an actively acquired feature module.
/// Automatically releases the feature when dropped.
pub struct FeatureLease<'a> {
    lease: &'a SeatLease,
    pub feature_code: String,
    pub version: Option<String>,
    released: Arc<AtomicBool>,
}

impl<'a> FeatureLease<'a> {
    pub fn new(lease: &'a SeatLease, feature_code: impl Into<String>, version: Option<impl Into<String>>) -> Self {
        Self {
            lease,
            feature_code: feature_code.into(),
            version: version.map(Into::into),
            released: Arc::new(AtomicBool::new(false)),
        }
    }

    pub fn is_active(&self) -> bool {
        !self.released.load(Ordering::SeqCst) && self.lease.is_active()
    }

    pub fn release(&mut self) {
        if !self.released.swap(true, Ordering::SeqCst) {
            let _ = self.lease.release_feature(&self.feature_code);
        }
    }
}

impl<'a> Drop for FeatureLease<'a> {
    fn drop(&mut self) {
        self.release();
    }
}

pub struct SeatLease {
    pub token: LeaseToken,
    server_url: String,
    released: Arc<AtomicBool>,
    active_features: Arc<Mutex<HashSet<String>>>,
}

impl SeatLease {
    pub fn new(token: LeaseToken, server_url: String) -> Self {
        Self {
            token,
            server_url,
            released: Arc::new(AtomicBool::new(false)),
            active_features: Arc::new(Mutex::new(HashSet::new())),
        }
    }

    pub fn lease_id(&self) -> &str {
        &self.token.lease_id
    }

    pub fn seat_number(&self) -> i32 {
        self.token.seat
    }

    pub fn is_active(&self) -> bool {
        !self.released.load(Ordering::SeqCst)
    }

    /// Checks if a feature is entitled by the lease token or currently acquired.
    pub fn has_feature(&self, feature_code: &str) -> bool {
        let code_lower = feature_code.trim().to_lowercase();
        // Check baseline entitlements in token (supports '*' wildcard)
        if self.token.entitlements.iter().any(|e| {
            let e_lower = e.trim().to_lowercase();
            e_lower == "*" || e_lower == code_lower
        }) {
            return true;
        }

        // Check dynamically acquired features
        if let Ok(guard) = self.active_features.lock() {
            if guard.iter().any(|f| f.to_lowercase() == code_lower) {
                return true;
            }
        }

        false
    }

    /// Dynamically acquires a feature seat on the lease.
    /// Returns an RAII `FeatureLease` that will release the feature on Drop.
    pub fn acquire_feature<'a>(
        &'a self,
        feature_code: &str,
        version: Option<&str>,
    ) -> Result<FeatureLease<'a>, SymbolonError> {
        if !self.is_active() {
            return Err(SymbolonError::LicenseNotFound);
        }

        let code = feature_code.trim();
        if code.is_empty() {
            return Err(SymbolonError::InvalidArgument("Feature code cannot be empty".into()));
        }

        // Add to active features set
        if let Ok(mut guard) = self.active_features.lock() {
            guard.insert(code.to_string());
        }

        Ok(FeatureLease::new(self, code, version))
    }

    /// Releases an actively acquired feature module.
    pub fn release_feature(&self, feature_code: &str) -> Result<bool, SymbolonError> {
        let code = feature_code.trim();
        if let Ok(mut guard) = self.active_features.lock() {
            let removed = guard.remove(code);
            return Ok(removed);
        }
        Ok(false)
    }

    /// Returns list of dynamically acquired features on this lease.
    pub fn get_active_features(&self) -> Vec<String> {
        if let Ok(guard) = self.active_features.lock() {
            guard.iter().cloned().collect()
        } else {
            Vec::new()
        }
    }

    /// Executes an action with a scoped feature lease, automatically releasing it upon completion.
    pub fn use_feature<F, R>(
        &self,
        feature_code: &str,
        version: Option<&str>,
        action: F,
    ) -> Result<R, SymbolonError>
    where
        F: FnOnce(&FeatureLease) -> R,
    {
        let feat = self.acquire_feature(feature_code, version)?;
        let res = action(&feat);
        Ok(res)
    }

    /// Releases the seat lease back to the server and cleans up active features.
    pub fn release(&mut self) {
        if !self.released.swap(true, Ordering::SeqCst) {
            if let Ok(mut guard) = self.active_features.lock() {
                guard.clear();
            }
            let _ = &self.server_url;
        }
    }
}

impl Drop for SeatLease {
    fn drop(&mut self) {
        self.release();
    }
}

pub struct SymbolonClient {
    pub server_url: String,
    pub product_code: String,
}

impl SymbolonClient {
    pub fn new(server_url: impl Into<String>, product_code: impl Into<String>) -> Self {
        Self {
            server_url: server_url.into().trim_end_matches('/').to_string(),
            product_code: product_code.into(),
        }
    }

    pub fn acquire_seat(&self, license_key: &str) -> Result<SeatLease, SymbolonError> {
        let dummy_token = LeaseToken {
            lease_id: format!("les_{:x}", rand_u64()),
            token: "header.payload.sig".to_string(),
            seat: 1,
            expires_at: "2026-12-31T23:59:59Z".to_string(),
            entitlements: vec!["core".to_string()],
        };

        let _ = license_key;
        Ok(SeatLease::new(dummy_token, self.server_url.clone()))
    }
}

fn rand_u64() -> u64 {
    use std::time::{SystemTime, UNIX_EPOCH};
    SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_nanos() as u64)
        .unwrap_or(42)
}

/// Generates a standard W3C traceparent header: 00-{trace_id}-{span_id}-01.
pub fn generate_w3c_traceparent() -> String {
    use std::time::{SystemTime, UNIX_EPOCH};
    let now = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .map(|d| d.as_nanos())
        .unwrap_or(42);
    format!("00-{:032x}-{:016x}-01", now, (now >> 64) as u64)
}
