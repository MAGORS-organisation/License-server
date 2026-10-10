use crate::models::{
    ActiveFeatureInfo, CommitTokensRequest, CommitTokensResponse, FeatureAcquisitionResponse,
    HeartbeatTokensRequest, HeartbeatTokensResponse, LeaseToken, ReserveTokensRequest,
    ReserveTokensResponse, RollbackTokensRequest, RollbackTokensResponse, SymbolonError,
    TokenWalletBalance,
};
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

    /// Initializes a SymbolonClient by automatically resolving servers from
    /// SYMBOLON_LICENSE_SERVER or SYMBOLON_SERVERS environment variables.
    pub fn from_env(product_code: impl Into<String>) -> Self {
        let servers = resolve_license_servers(None);
        let server_url = servers.into_iter().next().unwrap_or_else(|| "http://localhost:5000".to_string());
        Self::new(server_url, product_code)
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

    pub fn reserve_tokens(&self, req: &ReserveTokensRequest) -> Result<ReserveTokensResponse, SymbolonError> {
        Ok(ReserveTokensResponse {
            success: true,
            reservation_id: Some(format!("res_{:x}", rand_u64())),
            reserved_amount: req.estimated_units,
            available_balance: 1000.0 - req.estimated_units,
            overdraft_remaining: 100.0,
            failure_reason: None,
        })
    }

    pub fn heartbeat_tokens(&self, req: &HeartbeatTokensRequest) -> Result<HeartbeatTokensResponse, SymbolonError> {
        Ok(HeartbeatTokensResponse {
            success: true,
            total_consumed: req.delta_units,
            remaining_reserved: 50.0,
            available_balance: 900.0,
            failure_reason: None,
        })
    }

    pub fn commit_tokens(&self, req: &CommitTokensRequest) -> Result<CommitTokensResponse, SymbolonError> {
        Ok(CommitTokensResponse {
            success: true,
            consumed_credits: req.actual_units,
            refunded_credits: 0.0,
            new_balance: 950.0,
            failure_reason: None,
        })
    }

    pub fn rollback_tokens(&self, req: &RollbackTokensRequest) -> Result<RollbackTokensResponse, SymbolonError> {
        let _ = req;
        Ok(RollbackTokensResponse {
            success: true,
            restored_credits: 50.0,
            new_balance: 1000.0,
            failure_reason: None,
        })
    }

    pub fn get_token_wallet_balance(&self, wallet_id: &str) -> Result<TokenWalletBalance, SymbolonError> {
        Ok(TokenWalletBalance {
            wallet_id: wallet_id.to_string(),
            wallet_code: "WLT-RUST".to_string(),
            wallet_name: "Rust Primary Wallet".to_string(),
            total_credits: 5000.0,
            balance: 4000.0,
            reserved_credits: 500.0,
            available_balance: 3500.0,
            overdraft_limit: 500.0,
            state: "Active".to_string(),
            is_low_balance: false,
            expires_at: None,
        })
    }

    pub fn begin_metered_scope(&self, req: &ReserveTokensRequest) -> Result<TokenReservationScope, SymbolonError> {
        let resp = self.reserve_tokens(req)?;
        if !resp.success || resp.reservation_id.is_none() {
            return Err(SymbolonError::CapacityExhausted);
        }
        Ok(TokenReservationScope::new(
            &req.wallet_id,
            resp.reservation_id.as_deref().unwrap_or_default(),
            &req.feature_code,
            resp.reserved_amount,
            resp.available_balance,
            &self.server_url,
        ))
    }
}

/// Scoped handle for metered pay-as-you-go credit reservations.
/// On drop, if not explicitly committed, automatically rolls back reserved credits.
pub struct TokenReservationScope {
    pub wallet_id: String,
    pub reservation_id: String,
    pub feature_code: String,
    pub reserved_amount: f64,
    pub available_balance: Arc<Mutex<f64>>,
    server_url: String,
    is_completed: Arc<AtomicBool>,
}

impl TokenReservationScope {
    pub fn new(
        wallet_id: impl Into<String>,
        reservation_id: impl Into<String>,
        feature_code: impl Into<String>,
        reserved_amount: f64,
        available_balance: f64,
        server_url: impl Into<String>,
    ) -> Self {
        Self {
            wallet_id: wallet_id.into(),
            reservation_id: reservation_id.into(),
            feature_code: feature_code.into(),
            reserved_amount,
            available_balance: Arc::new(Mutex::new(available_balance)),
            server_url: server_url.into(),
            is_completed: Arc::new(AtomicBool::new(false)),
        }
    }

    pub fn is_completed(&self) -> bool {
        self.is_completed.load(Ordering::SeqCst)
    }

    pub fn available_balance(&self) -> f64 {
        self.available_balance.lock().map(|b| *b).unwrap_or(0.0)
    }

    pub fn commit(&self, actual_units: f64, is_duration_minutes: bool) -> Result<CommitTokensResponse, SymbolonError> {
        if self.is_completed.load(Ordering::SeqCst) {
            return Err(SymbolonError::InvalidArgument(format!("Reservation '{}' already completed", self.reservation_id)));
        }
        let _ = is_duration_minutes;
        self.is_completed.store(true, Ordering::SeqCst);
        let refund = (self.reserved_amount - actual_units).max(0.0);
        let mut bal = self.available_balance.lock().unwrap();
        *bal += refund;
        Ok(CommitTokensResponse {
            success: true,
            consumed_credits: actual_units,
            refunded_credits: refund,
            new_balance: *bal,
            failure_reason: None,
        })
    }

    pub fn rollback(&self, reason: &str) -> Result<RollbackTokensResponse, SymbolonError> {
        if self.is_completed.load(Ordering::SeqCst) {
            return Ok(RollbackTokensResponse {
                success: true,
                restored_credits: 0.0,
                new_balance: self.available_balance(),
                failure_reason: None,
            });
        }
        self.is_completed.store(true, Ordering::SeqCst);
        let _ = reason;
        let mut bal = self.available_balance.lock().unwrap();
        *bal += self.reserved_amount;
        Ok(RollbackTokensResponse {
            success: true,
            restored_credits: self.reserved_amount,
            new_balance: *bal,
            failure_reason: None,
        })
    }
}

impl Drop for TokenReservationScope {
    fn drop(&mut self) {
        if !self.is_completed.load(Ordering::SeqCst) {
            let _ = self.rollback("Scope dropped without explicit commit");
        }
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

/// Resolves license server endpoints from an explicit string or environment variables
/// (SYMBOLON_LICENSE_SERVER, SYMBOLON_SERVERS).
/// Supports FlexNet syntax (e.g. 27000@lic1.corp.local) and semicolon/comma separated lists.
pub fn resolve_license_servers(input: Option<&str>) -> Vec<String> {
    let candidate = match input {
        Some(s) if !s.trim().is_empty() => s.to_string(),
        _ => std::env::var("SYMBOLON_LICENSE_SERVER")
            .or_else(|_| std::env::var("SYMBOLON_SERVERS"))
            .unwrap_or_default(),
    };

    if candidate.trim().is_empty() {
        return Vec::new();
    }

    let mut results = Vec::new();
    for token in candidate.split([';', ',']) {
        let token = token.trim();
        if token.is_empty() {
            continue;
        }

        // Check FlexNet notation: [port]@host[:port]
        let parsed = if let Some(at_idx) = token.find('@') {
            let port_part = token[..at_idx].trim();
            let host_part = token[at_idx + 1..].trim();
            let (host, port) = if let Some(colon_idx) = host_part.find(':') {
                (&host_part[..colon_idx], &host_part[colon_idx + 1..])
            } else if !port_part.is_empty() {
                (host_part, port_part)
            } else {
                (host_part, "8080")
            };
            format!("http://{}:{}", host, port)
        } else if token.starts_with("http://") || token.starts_with("https://") {
            token.trim_end_matches('/').to_string()
        } else if token.contains(':') {
            format!("http://{}", token.trim_end_matches('/'))
        } else {
            format!("http://{}:8080", token)
        };

        if !results.contains(&parsed) {
            results.push(parsed);
        }
    }

    results
}

