use sha2::{Digest, Sha256};
use std::collections::BTreeMap;

pub fn get_hardware_components() -> BTreeMap<String, String> {
    let mut map = BTreeMap::new();
    map.insert("os".to_string(), std::env::consts::OS.to_string());
    map.insert("arch".to_string(), std::env::consts::ARCH.to_string());
    map.insert(
        "hostname".to_string(),
        std::env::var("COMPUTERNAME")
            .or_else(|_| std::env::var("HOSTNAME"))
            .unwrap_or_else(|_| "unknown-host".to_string())
            .to_lowercase(),
    );
    map
}

pub fn compute_canonical_fingerprint(components: &BTreeMap<String, String>) -> String {
    // Canonical JSON: sorted keys (BTreeMap automatically guarantees this)
    let json_bytes = serde_json::to_vec(components).unwrap_or_default();
    let mut hasher = Sha256::new();
    hasher.update(&json_bytes);
    let hash = hasher.finalize();
    format!("sha256:{}", hex::encode(hash))
}

pub fn get_local_fingerprint() -> String {
    compute_canonical_fingerprint(&get_hardware_components())
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_canonical_fingerprint() {
        let mut map = BTreeMap::new();
        map.insert("os".to_string(), "linux".to_string());
        map.insert("arch".to_string(), "x86_64".to_string());
        let fp = compute_canonical_fingerprint(&map);
        assert!(fp.starts_with("sha256:"));
        assert_eq!(fp.len(), 71);
    }
}
