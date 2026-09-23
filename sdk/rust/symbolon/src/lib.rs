//! Official Rust client SDK for the Symbolon Floating & Enterprise License Server.

pub mod attestation;
pub mod client;
pub mod fingerprint;
pub mod models;

pub use attestation::{EnclaveType, HardwareAttestationQuote, TpmQuoteGenerator, TpmQuoteVerifier};
pub use client::{FeatureLease, SeatLease, SymbolonClient};
pub use fingerprint::{compute_canonical_fingerprint, get_local_fingerprint};
pub use models::{ActiveFeatureInfo, FeatureAcquisitionResponse, LeaseToken, ReleaseFeatureResponse, SymbolonError};
