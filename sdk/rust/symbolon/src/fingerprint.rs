use sha2::{Digest, Sha256};
use std::collections::BTreeMap;
use std::fs;
use std::path::{Path, PathBuf};

/// Detects if running inside a container, Kubernetes pod, or cloud environment (FPR-10, FPR-11).
pub fn is_container_or_cloud() -> bool {
    // 1. Container marker files
    if Path::new("/.dockerenv").exists() || Path::new("/run/.containerenv").exists() {
        return true;
    }

    // 2. Linux /proc/1/cgroup inspection
    if let Ok(cgroup) = fs::read_to_string("/proc/1/cgroup") {
        let lower = cgroup.to_lowercase();
        if lower.contains("docker")
            || lower.contains("containerd")
            || lower.contains("kubepods")
            || lower.contains("lxc")
        {
            return true;
        }
    }

    // 3. Container and Cloud environment variables (FPR-11)
    let cloud_vars = [
        "KUBERNETES_SERVICE_HOST",
        "container",
        "DOTNET_RUNNING_IN_CONTAINER",
        "AWS_EXECUTION_ENV",
        "ECS_CONTAINER_METADATA_URI",
        "AZURE_CONTAINER_APP_NAME",
        "GOOGLE_CLOUD_PROJECT",
    ];
    for var in &cloud_vars {
        if std::env::var(var).is_ok() {
            return true;
        }
    }

    false
}

/// Generates a RFC 4122 v4 UUID from nanosecond timestamp and entropy.
fn generate_uuid() -> String {
    use std::time::{SystemTime, UNIX_EPOCH};
    let now = SystemTime::now()
        .duration_since(UNIX_EPOCH)
        .unwrap_or_default()
        .as_nanos();
    let pid = std::process::id();
    let mut hasher = Sha256::new();
    hasher.update(&now.to_le_bytes());
    hasher.update(&pid.to_le_bytes());
    let mut b = hasher.finalize();
    b[6] = (b[6] & 0x0f) | 0x40; // RFC 4122 version 4
    b[8] = (b[8] & 0x3f) | 0x80; // RFC 4122 variant
    let h = hex::encode(b);
    format!("{}-{}-{}-{}-{}", &h[0..8], &h[8..12], &h[12..16], &h[16..20], &h[20..32])
}

/// Retrieves or persists a random UUID in volume storage for container environments (FPR-12).
pub fn get_or_create_persisted_container_uuid(custom_volume_path: Option<&str>) -> String {
    let path: PathBuf = if let Some(p) = custom_volume_path {
        PathBuf::from(p)
    } else {
        let base = std::env::var("LOCALAPPDATA")
            .map(|l| PathBuf::from(l).join("symbolon"))
            .unwrap_or_else(|_| {
                std::env::var("HOME")
                    .map(|h| PathBuf::from(h).join(".symbolon"))
                    .unwrap_or_else(|_| std::env::temp_dir().join(".symbolon"))
            });
        let _ = fs::create_dir_all(&base);
        base.join("container_instance_uuid.txt")
    };

    if let Ok(content) = fs::read_to_string(&path) {
        let trimmed = content.trim().to_string();
        if trimmed.len() >= 32 {
            return trimmed;
        }
    }

    let new_uuid = generate_uuid();
    let _ = fs::write(&path, &new_uuid);
    new_uuid
}

/// Gathers hardware components or container UUID according to FPR-1, FPR-10, FPR-12.
pub fn get_hardware_components() -> BTreeMap<String, String> {
    get_hardware_components_with_volume(None)
}

/// Gathers hardware components with optional custom volume path for container UUID.
pub fn get_hardware_components_with_volume(custom_volume_path: Option<&str>) -> BTreeMap<String, String> {
    let mut map = BTreeMap::new();

    if is_container_or_cloud() {
        // FPR-12: In container/cloud, NEVER use hardware
        // FPR-13: Log recommendation to use floating license with short lease TTL
        eprintln!("WARNING: Containerized or cloud environment detected. Hardware node-locking is an anti-pattern in containers. Recommended: floating license with short lease TTL (FPR-13).");
        map.insert(
            "machineId".to_string(),
            get_or_create_persisted_container_uuid(custom_volume_path),
        );
        map.insert("isContainer".to_string(), "true".to_string());
        map.insert("os".to_string(), std::env::consts::OS.to_string());
        map.insert("arch".to_string(), std::env::consts::ARCH.to_string());
        return map;
    }

    map.insert("os".to_string(), std::env::consts::OS.to_string());
    map.insert("arch".to_string(), std::env::consts::ARCH.to_string());
    map.insert(
        "hostname".to_string(),
        std::env::var("COMPUTERNAME")
            .or_else(|_| std::env::var("HOSTNAME"))
            .unwrap_or_else(|_| "unknown-host".to_string())
            .to_lowercase(),
    );
    let machine_id = std::env::var("SYMBOLON_MACHINE_ID")
        .or_else(|_| std::env::var("COMPUTERNAME"))
        .unwrap_or_else(|_| "localhost".to_string());
    map.insert("machineId".to_string(), machine_id);
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

    #[test]
    fn test_container_uuid_persistence() {
        let temp_dir = std::env::temp_dir();
        let test_path = temp_dir.join("symbolon_rust_test_uuid.txt");
        let path_str = test_path.to_str().unwrap();
        let _ = fs::remove_file(&test_path);

        let uuid1 = get_or_create_persisted_container_uuid(Some(path_str));
        assert!(uuid1.len() >= 32);

        let uuid2 = get_or_create_persisted_container_uuid(Some(path_str));
        assert_eq!(uuid1, uuid2);

        let _ = fs::remove_file(&test_path);
    }
}
