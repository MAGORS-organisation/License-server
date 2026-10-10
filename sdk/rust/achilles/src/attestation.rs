//! Hardware Enclave Attestation and TPM 2.0 PCR quote validation.

use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};

/// Hardware Enclave and Root-of-Trust type.
#[derive(Debug, Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
#[repr(u32)]
pub enum EnclaveType {
    None = 0,
    Tpm20 = 1,
    IntelSgx = 2,
    AmdSevSnp = 3,
}

impl From<u32> for EnclaveType {
    fn from(val: u32) -> Self {
        match val {
            1 => EnclaveType::Tpm20,
            2 => EnclaveType::IntelSgx,
            3 => EnclaveType::AmdSevSnp,
            _ => EnclaveType::None,
        }
    }
}

/// Cryptographic Hardware Attestation Quote representation.
#[derive(Debug, Clone, Serialize, Deserialize)]
pub struct HardwareAttestationQuote {
    #[serde(rename = "enclaveType")]
    pub enclave_type: u32,
    #[serde(rename = "aikId")]
    pub aik_id: String,
    pub nonce: String,
    #[serde(rename = "pcrIndices")]
    pub pcr_indices: Vec<u32>,
    #[serde(rename = "pcrDigest")]
    pub pcr_digest: String,
    #[serde(rename = "quoteData")]
    pub quote_data: String,
    pub signature: String,
    #[serde(rename = "signatureAlgorithm")]
    pub signature_algorithm: String,
    pub timestamp: String,
}

/// Generator for Hardware Attestation Quotes (TPM 2.0 / Enclaves).
pub struct TpmQuoteGenerator;

impl TpmQuoteGenerator {
    pub fn create_quote(
        aik_id: &str,
        secret: &str,
        nonce: &str,
        pcr_indices: &[u32],
        enclave_type: EnclaveType,
    ) -> HardwareAttestationQuote {
        let mut sorted_indices = pcr_indices.to_vec();
        sorted_indices.sort_unstable();

        let mut pcr_repr = String::new();
        for (i, &idx) in sorted_indices.iter().enumerate() {
            if i > 0 {
                pcr_repr.push(';');
            }
            pcr_repr.push_str(&format!("{idx}:pcr_baseline_val_{idx}"));
        }

        let pcr_hash = Sha256::digest(pcr_repr.as_bytes());
        let pcr_digest = format!("sha256:{}", hex::encode(pcr_hash));

        let timestamp = "2026-09-22T10:00:00Z".to_string();
        let quote_data = format!(
            "TPMS_ATTEST:{}:{}:{}:{}:{}",
            enclave_type as u32, aik_id, nonce, pcr_digest, timestamp
        );

        let sig_bytes = compute_hmac_sha256(secret.as_bytes(), quote_data.as_bytes());
        let signature = hex::encode(sig_bytes);

        HardwareAttestationQuote {
            enclave_type: enclave_type as u32,
            aik_id: aik_id.to_string(),
            nonce: nonce.to_string(),
            pcr_indices: sorted_indices,
            pcr_digest,
            quote_data,
            signature,
            signature_algorithm: "HMAC-SHA256".to_string(),
            timestamp,
        }
    }
}

/// Verifier for Hardware Attestation Quotes against anti-replay nonces and PCR policies.
pub struct TpmQuoteVerifier;

impl TpmQuoteVerifier {
    pub fn verify_quote(
        quote: &HardwareAttestationQuote,
        expected_nonce: &str,
        secret: &str,
        required_pcr_indices: Option<&[u32]>,
    ) -> bool {
        // 1. Anti-replay verification
        if !constant_time_eq(quote.nonce.as_bytes(), expected_nonce.as_bytes()) {
            return false;
        }

        // 2. Required PCR indices verification
        if let Some(required) = required_pcr_indices {
            for &req in required {
                if !quote.pcr_indices.contains(&req) {
                    return false;
                }
            }
        }

        // 3. Signature verification
        let expected_sig_bytes = compute_hmac_sha256(secret.as_bytes(), quote.quote_data.as_bytes());
        let expected_sig_hex = hex::encode(expected_sig_bytes);

        constant_time_eq(
            quote.signature.to_lowercase().as_bytes(),
            expected_sig_hex.to_lowercase().as_bytes(),
        )
    }
}

/// Computes HMAC-SHA256 according to RFC 2104 using pure `sha2`.
fn compute_hmac_sha256(key: &[u8], message: &[u8]) -> [u8; 32] {
    let mut k_prime = [0u8; 64];
    if key.len() > 64 {
        let hash = Sha256::digest(key);
        k_prime[..32].copy_from_slice(&hash);
    } else {
        k_prime[..key.len()].copy_from_slice(key);
    }

    let mut o_key_pad = [0u8; 64];
    let mut i_key_pad = [0u8; 64];
    for i in 0..64 {
        o_key_pad[i] = k_prime[i] ^ 0x5c;
        i_key_pad[i] = k_prime[i] ^ 0x36;
    }

    let mut inner_hasher = Sha256::new();
    inner_hasher.update(i_key_pad);
    inner_hasher.update(message);
    let inner_hash = inner_hasher.finalize();

    let mut outer_hasher = Sha256::new();
    outer_hasher.update(o_key_pad);
    outer_hasher.update(inner_hash);
    outer_hasher.finalize().into()
}

/// Constant-time comparison to prevent timing attacks.
fn constant_time_eq(a: &[u8], b: &[u8]) -> bool {
    if a.len() != b.len() {
        return false;
    }
    let mut diff = 0u8;
    for (x, y) in a.iter().zip(b.iter()) {
        diff |= x ^ y;
    }
    diff == 0
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_valid_quote_generation_and_verification() {
        let quote = TpmQuoteGenerator::create_quote(
            "aik-01",
            "secret-key-123",
            "fresh_nonce",
            &[0, 1, 7],
            EnclaveType::Tpm20,
        );

        assert_eq!(quote.enclave_type, 1);
        assert_eq!(quote.nonce, "fresh_nonce");

        let valid = TpmQuoteVerifier::verify_quote(
            &quote,
            "fresh_nonce",
            "secret-key-123",
            Some(&[0, 7]),
        );
        assert!(valid);
    }

    #[test]
    fn test_anti_replay_nonce_rejection() {
        let quote = TpmQuoteGenerator::create_quote(
            "aik-01",
            "secret-key-123",
            "fresh_nonce",
            &[0, 1, 7],
            EnclaveType::Tpm20,
        );

        // Stale nonce (replay attack)
        let valid = TpmQuoteVerifier::verify_quote(
            &quote,
            "stale_nonce",
            "secret-key-123",
            None,
        );
        assert!(!valid);
    }

    #[test]
    fn test_wrong_secret_rejection() {
        let quote = TpmQuoteGenerator::create_quote(
            "aik-01",
            "secret-key-123",
            "fresh_nonce",
            &[0, 1, 7],
            EnclaveType::Tpm20,
        );

        let valid = TpmQuoteVerifier::verify_quote(
            &quote,
            "fresh_nonce",
            "wrong-secret",
            None,
        );
        assert!(!valid);
    }

    #[test]
    fn test_missing_required_pcr_rejection() {
        let quote = TpmQuoteGenerator::create_quote(
            "aik-01",
            "secret-key-123",
            "fresh_nonce",
            &[0, 1], // Missing PCR 7
            EnclaveType::Tpm20,
        );

        let valid = TpmQuoteVerifier::verify_quote(
            &quote,
            "fresh_nonce",
            "secret-key-123",
            Some(&[0, 7]),
        );
        assert!(!valid);
    }
}
