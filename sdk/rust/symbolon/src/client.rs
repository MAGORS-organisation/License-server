use crate::models::{LeaseToken, SymbolonError};
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Arc;

pub struct SeatLease {
    pub token: LeaseToken,
    server_url: String,
    released: Arc<AtomicBool>,
}

impl SeatLease {
    pub fn new(token: LeaseToken, server_url: String) -> Self {
        Self {
            token,
            server_url,
            released: Arc::new(AtomicBool::new(false)),
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

    pub fn release(&mut self) {
        if !self.released.swap(true, Ordering::SeqCst) {
            // Best effort release
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
