"""Hardware Fingerprint Generation according to Symbolon spec/08-fingerprint.md."""

import hashlib
import json
import os
import platform
import socket
import uuid
from typing import Dict


def get_hardware_components() -> Dict[str, str]:
    """Extract standard machine identification components."""
    components = {
        "os": platform.system().lower(),
        "arch": platform.machine().lower(),
        "hostname": socket.gethostname().lower(),
    }

    try:
        node = uuid.getnode()
        components["mac"] = f"{(node >> 40) & 0xff:02x}:{(node >> 32) & 0xff:02x}:{(node >> 24) & 0xff:02x}:{(node >> 16) & 0xff:02x}:{(node >> 8) & 0xff:02x}:{node & 0xff:02x}"
    except Exception:
        components["mac"] = "00:00:00:00:00:00"

    return components


def compute_canonical_fingerprint(components: Dict[str, str]) -> str:
    """Computes SHA-256 hash over canonical (alphabetically sorted keys) JSON representation."""
    canonical_json = json.dumps(components, sort_keys=True, separators=(",", ":"))
    sha256_hash = hashlib.sha256(canonical_json.encode("utf-8")).hexdigest()
    return f"sha256:{sha256_hash}"


def get_local_fingerprint() -> str:
    """Convenience function returning the local machine's canonical fingerprint."""
    return compute_canonical_fingerprint(get_hardware_components())
