//! Zero-allocation offline token parser and validator for Symbolon license tokens.

use crate::models::SymbolonError;
use sha2::{Digest, Sha256};

/// Representation of a parsed 3-part compact license token without heap allocations.
#[derive(Debug, Clone, Copy, PartialEq, Eq)]
pub struct ParsedToken<'a> {
    pub header: &'a str,
    pub payload: &'a str,
    pub signature: &'a str,
    pub signing_input: &'a str,
}

impl<'a> ParsedToken<'a> {
    /// Parses a raw compact token `header.payload.signature` into zero-allocation slices.
    pub fn parse(raw: &'a str) -> Result<Self, SymbolonError> {
        let first_dot = raw.find('.').ok_or_else(|| {
            SymbolonError::LicenseInvalid("Missing first dot separator in token".into())
        })?;

        let rest = &raw[first_dot + 1..];
        let second_dot = rest.find('.').ok_or_else(|| {
            SymbolonError::LicenseInvalid("Missing second dot separator in token".into())
        })?;

        let header = &raw[..first_dot];
        let payload = &rest[..second_dot];
        let signature = &rest[second_dot + 1..];

        if header.is_empty() || payload.is_empty() || signature.is_empty() {
            return Err(SymbolonError::LicenseInvalid("Token contains empty segments".into()));
        }

        let signing_input = &raw[..first_dot + 1 + second_dot];

        Ok(Self {
            header,
            payload,
            signature,
            signing_input,
        })
    }

    /// Computes the SHA-256 digest of the signing input without allocating intermediate buffers.
    pub fn compute_digest(&self) -> [u8; 32] {
        let mut hasher = Sha256::new();
        hasher.update(self.signing_input.as_bytes());
        hasher.finalize().into()
    }

    /// Checks if a feature flag is present in the raw payload slice without full JSON deserialization.
    pub fn contains_feature(&self, feature_name: &str) -> bool {
        self.payload.contains(feature_name)
    }

    /// Validates expiration timestamp against current UNIX epoch seconds.
    pub fn is_expired(&self, current_unix_epoch: u64) -> bool {
        // Fast scan for `"exp":` in payload slice
        if let Some(pos) = self.payload.find("\"exp\":") {
            let num_slice = &self.payload[pos + 6..];
            let end = num_slice
                .find(|c: char| !c.is_ascii_digit())
                .unwrap_or(num_slice.len());
            if let Ok(exp) = num_slice[..end].parse::<u64>() {
                return current_unix_epoch > exp;
            }
        }
        false
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_parse_valid_token() {
        let raw = "eyJhbGciOiJFUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwic2VhdHMiOjUsImV4cCI6MTk5OTk5OTk5OSwiZmVhdHVyZXMiOlsicHJvIl19.c2lnbmF0dXJl";
        let token = ParsedToken::parse(raw).expect("Should parse token");

        assert_eq!(token.header, "eyJhbGciOiJFUzI1NiJ9");
        assert_eq!(token.signature, "c2lnbmF0dXJl");
        assert_eq!(token.signing_input, "eyJhbGciOiJFUzI1NiJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwic2VhdHMiOjUsImV4cCI6MTk5OTk5OTk5OSwiZmVhdHVyZXMiOlsicHJvIl19");
        assert!(token.contains_feature("pro"));
        assert!(!token.contains_feature("enterprise_ai"));
        assert!(!token.is_expired(1700000000));
        assert!(token.is_expired(2100000000));
    }

    #[test]
    fn test_parse_invalid_token() {
        assert!(ParsedToken::parse("single-part").is_err());
        assert!(ParsedToken::parse("header.payload").is_err());
        assert!(ParsedToken::parse("header..signature").is_err());
    }
}
