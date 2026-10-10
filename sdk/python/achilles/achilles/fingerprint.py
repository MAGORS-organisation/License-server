"""
Multi-Component Hardware Fingerprinting, Fuzzy Matching & Container Isolation (spec/08-fingerprint.md, FPR-1 to FPR-14).
"""

import hashlib
import os
import platform
import socket
import sys
import uuid
from typing import Dict, List, Optional, Tuple, NamedTuple


PLACEHOLDER_VALUES = {
    "unknown",
    "none",
    "0",
    "00000000-0000-0000-0000-000000000000",
    "null",
    "n/a",
    "default",
    "to be filled by o.e.m.",
    "not specified",
    "system serial number",
    "00:00:00:00:00:00",
}


class MatchResult(NamedTuple):
    is_match: bool
    strategy_used: str
    common_count: int
    matched_count: int
    matched_keys: List[str]
    mismatched_keys: List[str]
    failure_reason: Optional[str] = None


def is_container_or_cloud() -> bool:
    """Detect if running inside Docker, Kubernetes pod, or cloud environment (FPR-10, FPR-11)."""
    if os.path.exists("/.dockerenv") or os.path.exists("/run/.containerenv"):
        return True

    try:
        if os.path.exists("/proc/1/cgroup"):
            with open("/proc/1/cgroup", "r", encoding="utf-8", errors="ignore") as f:
                content = f.read().lower()
                if any(x in content for x in ("docker", "containerd", "kubepods", "lxc")):
                    return True
    except Exception:
        pass

    cloud_env_vars = (
        "KUBERNETES_SERVICE_HOST",
        "container",
        "DOTNET_RUNNING_IN_CONTAINER",
        "AWS_EXECUTION_ENV",
        "ECS_CONTAINER_METADATA_URI",
        "AZURE_CONTAINER_APP_NAME",
        "GOOGLE_CLOUD_PROJECT",
    )
    if any(os.environ.get(var) for var in cloud_env_vars):
        return True

    return False


def filter_valid_components(components: Optional[Dict[str, str]]) -> Dict[str, str]:
    """Filter out unavailable or placeholder components according to FPR-3."""
    if not components:
        return {}
    filtered = {}
    for k, v in components.items():
        if not k or not v:
            continue
        clean_k = str(k).strip()
        clean_v = str(v).strip()
        if not clean_k or not clean_v:
            continue
        if clean_v.lower() in PLACEHOLDER_VALUES:
            continue
        filtered[clean_k] = clean_v
    return filtered


def pseudonymize_host(raw_host: str, license_salt: str) -> str:
    """Pseudonymizes hostname with license salt according to FPR-2."""
    clean_host = raw_host.strip().upper()
    clean_salt = license_salt.strip()
    payload = f"{clean_salt}:{clean_host}".encode("utf-8")
    return f"sha256:{hashlib.sha256(payload).hexdigest()}"


def canonicalize(components: Optional[Dict[str, str]]) -> str:
    """Format components canonically according to FPR-4 (alphabetical uppercase keys)."""
    valid = filter_valid_components(components)
    lines = []
    for k in sorted(valid.keys(), key=lambda x: x.lower()):
        lines.append(f"{k.strip().upper()}={valid[k].strip()}\n")
    return "".join(lines)


def compute_canonical_fingerprint(components: Optional[Dict[str, str]]) -> str:
    """Computes sha256:{hex} summary hash of components according to FPR-4."""
    canonical_str = canonicalize(components)
    digest = hashlib.sha256(canonical_str.encode("utf-8")).hexdigest()
    return f"sha256:{digest}"


def get_local_fingerprint() -> str:
    """Convenience function returning the local machine's canonical fingerprint."""
    return compute_canonical_fingerprint(get_hardware_components())


def get_or_create_persisted_container_uuid(custom_path: Optional[str] = None) -> str:
    """Persists a stable random UUID in volume storage for containerized environments (FPR-12)."""
    if custom_path:
        path = custom_path
    else:
        app_data = os.environ.get("LOCALAPPDATA") or os.path.expanduser("~/.symbolon")
        os.makedirs(app_data, exist_ok=True)
        path = os.path.join(app_data, "container_instance_uuid.txt")

    try:
        if os.path.exists(path):
            with open(path, "r", encoding="utf-8") as f:
                val = f.read().strip()
                if val:
                    uuid.UUID(val)  # validate
                    return val
        new_uuid = str(uuid.uuid4())
        with open(path, "w", encoding="utf-8") as f:
            f.write(new_uuid)
        return new_uuid
    except Exception:
        return f"uuid-{uuid.uuid4().hex}"


def get_hardware_components(license_salt: Optional[str] = None, custom_volume_path: Optional[str] = None) -> Dict[str, str]:
    """Collect hardware components or container UUID according to FPR-1, FPR-2, FPR-10 - FPR-13."""
    if is_container_or_cloud():
        # FPR-12: In container/cloud, NEVER use hardware
        persisted = get_or_create_persisted_container_uuid(custom_volume_path)
        comps = {
            "machineId": persisted,
            "isContainer": "true"
        }
        if license_salt:
            comps["host"] = pseudonymize_host(socket.gethostname(), license_salt)
        return filter_valid_components(comps)

    components = {}

    # 1. machineId
    system = platform.system().lower()
    if system == "windows":
        try:
            import winreg
            with winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, r"SOFTWARE\Microsoft\Cryptography") as key:
                val, _ = winreg.QueryValueEx(key, "MachineGuid")
                if val:
                    components["machineId"] = str(val).strip()
        except Exception:
            components["machineId"] = socket.gethostname()
    elif system == "linux":
        for p in ("/etc/machine-id", "/var/lib/dbus/machine-id"):
            if os.path.exists(p):
                try:
                    with open(p, "r", encoding="utf-8") as f:
                        val = f.read().strip()
                        if val:
                            components["machineId"] = val
                            break
                except Exception:
                    pass
    elif system == "darwin":
        try:
            import subprocess
            out = subprocess.check_output(["ioreg", "-rd1", "-c", "IOPlatformExpertDevice"]).decode("utf-8")
            for line in out.splitlines():
                if "IOPlatformUUID" in line:
                    parts = line.split("=")
                    if len(parts) == 2:
                        components["machineId"] = parts[1].strip(' ";')
        except Exception:
            pass

    if "machineId" not in components:
        components["machineId"] = socket.gethostname()

    # 2. cpu
    cpu = platform.processor()
    if cpu:
        components["cpu"] = cpu

    # 3. mac
    try:
        node = uuid.getnode()
        mac_str = f"{(node >> 40) & 0xff:02X}:{(node >> 32) & 0xff:02X}:{(node >> 24) & 0xff:02X}:{(node >> 16) & 0xff:02X}:{(node >> 8) & 0xff:02X}:{node & 0xff:02X}"
        if mac_str != "00:00:00:00:00:00":
            components["mac"] = mac_str
    except Exception:
        pass

    # 4. host
    raw_host = socket.gethostname()
    if raw_host:
        if license_salt:
            components["host"] = pseudonymize_host(raw_host, license_salt)
        else:
            components["host"] = raw_host

    # OS / arch metadata
    components["os"] = system
    components["arch"] = platform.machine()

    return filter_valid_components(components)


def evaluate_fingerprint_match(
    stored_components: Optional[Dict[str, str]],
    incoming_components: Optional[Dict[str, str]],
    strategy: str = "match-most"
) -> MatchResult:
    """Evaluates machine matching according to FPR-5 through FPR-9."""
    effective_strategy = (strategy or "match-most").strip().lower()
    valid_strategies = ("match-any", "match-two", "match-most", "match-all")
    if effective_strategy not in valid_strategies:
        effective_strategy = "match-most"

    clean_stored = filter_valid_components(stored_components)
    clean_incoming = filter_valid_components(incoming_components)

    stored_norm = {k.lower(): (k, v) for k, v in clean_stored.items()}
    incoming_norm = {k.lower(): (k, v) for k, v in clean_incoming.items()}

    common_keys = sorted(set(stored_norm.keys()) & set(incoming_norm.keys()))

    if not common_keys:
        return MatchResult(
            is_match=False,
            strategy_used=effective_strategy,
            common_count=0,
            matched_count=0,
            matched_keys=[],
            mismatched_keys=[],
            failure_reason="No common components found to compare (FPR-7)."
        )

    matched = []
    mismatched = []
    for k in common_keys:
        orig_key_stored, val_stored = stored_norm[k]
        _, val_incoming = incoming_norm[k]
        if val_stored.strip().lower() == val_incoming.strip().lower():
            matched.append(orig_key_stored)
        else:
            mismatched.append(orig_key_stored)

    common_count = len(common_keys)
    matched_count = len(matched)

    if effective_strategy == "match-any":
        is_match = matched_count >= 1
        reason = None if is_match else f"Expected at least 1 match under match-any, matched 0/{common_count}."
    elif effective_strategy == "match-two":
        is_match = matched_count >= 2
        reason = None if is_match else f"Expected at least 2 matches under match-two, matched {matched_count}/{common_count}."
    elif effective_strategy == "match-most":
        if common_count < 2:
            is_match = matched_count == common_count
            reason = None if is_match else f"Common count ({common_count}) < 2; degraded to match-all under FPR-8 and matched {matched_count}/{common_count}."
        else:
            is_match = matched_count > (common_count / 2.0)
            reason = None if is_match else f"Expected majority (> {common_count / 2.0:.1f}), matched {matched_count}/{common_count}."
    elif effective_strategy == "match-all":
        is_match = matched_count == common_count
        reason = None if is_match else f"Expected all {common_count} to match, but {len(mismatched)} mismatched."
    else:
        is_match = matched_count > (common_count / 2.0)
        reason = None

    return MatchResult(
        is_match=is_match,
        strategy_used=effective_strategy,
        common_count=common_count,
        matched_count=matched_count,
        matched_keys=matched,
        mismatched_keys=mismatched,
        failure_reason=reason
    )
