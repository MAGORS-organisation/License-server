//! Official Rust client SDK for the Symbolon Floating & Enterprise License Server.

pub mod client;
pub mod fingerprint;
pub mod models;

pub use client::{SeatLease, SymbolonClient};
pub use fingerprint::{compute_canonical_fingerprint, get_local_fingerprint};
pub use models::{LeaseToken, SymbolonError};
