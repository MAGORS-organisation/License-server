"""Symbolon ISV Client for Python with automated background heartbeats and jitter."""

import json
import random
import threading
import time
import urllib.request
import urllib.error
from contextlib import contextmanager
from datetime import datetime, timezone
from typing import Dict, List, Optional, Union

from .models import LeaseToken, SymbolonException, SeatAllocationDenied, FeatureDenied
from .fingerprint import get_hardware_components, compute_canonical_fingerprint


def _generate_w3c_traceparent() -> str:
    """Generates a compliant W3C traceparent header: 00-{trace_id}-{span_id}-01."""
    trace_id = f"{random.getrandbits(128):032x}"
    span_id = f"{random.getrandbits(64):016x}"
    return f"00-{trace_id}-{span_id}-01"


class FeatureLease:
    """Represents an acquired feature entitlement with RAII context manager support."""

    def __init__(self, parent: "SeatLease", feature_code: str, version: Optional[str] = None):
        self._parent = parent
        self.feature_code = feature_code
        self.version = version
        self._is_released = False

    @property
    def is_active(self) -> bool:
        return not self._is_released and self._parent.is_active

    def release(self) -> bool:
        if not self._is_released:
            self._is_released = True
            return self._parent.release_feature(self.feature_code)
        return False

    def __enter__(self) -> "FeatureLease":
        return self

    def __exit__(self, exc_type, exc_val, exc_tb) -> None:
        self.release()


class SeatLease:
    """Represents an acquired floating seat lease. Supports Python context manager."""

    def __init__(self, client: "SymbolonClient", token: LeaseToken, seq: int = 0):
        self._client = client
        self.token = token
        self.seq = seq
        self._is_released = False
        self._is_borrowed = False
        self._active_features: Dict[str, FeatureLease] = {}

    @property
    def lease_id(self) -> str:
        return self.token.lease_id

    @property
    def seat_number(self) -> int:
        return self.token.seat_number

    @property
    def is_active(self) -> bool:
        return not self._is_released

    @property
    def is_borrowed(self) -> bool:
        return self._is_borrowed

    def has_feature(self, feature_code: str) -> bool:
        """Returns True if the feature was included in entitlements or acquired dynamically."""
        if not feature_code:
            return False
        code_lower = feature_code.lower()
        if any(e.lower() == code_lower for e in self.token.entitlements):
            return True
        return any(k.lower() == code_lower for k in self._active_features.keys())

    def acquire_feature(
        self,
        feature_code: str,
        version: Optional[str] = None,
        ttl_seconds: Optional[int] = None,
    ) -> FeatureLease:
        """Dynamically acquires a feature seat on the server."""
        self._client.acquire_feature(self.lease_id, feature_code, version, ttl_seconds)
        feat_lease = FeatureLease(self, feature_code, version)
        self._active_features[feature_code] = feat_lease
        return feat_lease

    def release_feature(self, feature_code: str) -> bool:
        """Releases an acquired feature seat."""
        self._active_features.pop(feature_code, None)
        return self._client.release_feature(self.lease_id, feature_code)

    @contextmanager
    def use_feature(
        self,
        feature_code: str,
        version: Optional[str] = None,
        ttl_seconds: Optional[int] = None,
    ):
        """RAII context manager: acquires feature on entry, releases on exit."""
        feat = self.acquire_feature(feature_code, version, ttl_seconds)
        try:
            yield feat
        finally:
            feat.release()

    def borrow(self, days: int) -> dict:
        """Borrows this seat for offline use for the specified number of days."""
        res = self._client.borrow_seat(self, days)
        self._is_borrowed = True
        return res

    def release(self) -> None:
        """Explicitly releases the allocated floating seat back to the pool."""
        if not self._is_released:
            self._client._stop_heartbeat(self.lease_id)
            self._client.release_seat(self.lease_id)
            self._is_released = True
            self._is_borrowed = False

    def __enter__(self) -> "SeatLease":
        return self

    def __exit__(self, exc_type, exc_val, exc_tb) -> None:
        if not self._is_borrowed:
            self.release()


class SymbolonClient:
    """Official Symbolon Client SDK for Python applications."""

    def __init__(
        self,
        server_url: Optional[str] = None,
        product_code: str = "",
        relay_url: Optional[str] = None,
        heartbeat_interval: float = 30.0,
        jitter_factor: float = 0.10,
        timeout: float = 10.0,
        server_urls: Optional[List[str]] = None,
    ):
        servers = list(server_urls) if server_urls else []
        if server_url:
            resolved = resolve_license_servers(server_url, fallback_to_env=False)
            servers.extend(resolved if resolved else [server_url.rstrip("/")])
        elif not servers:
            servers = resolve_license_servers(fallback_to_env=True)

        if not servers:
            servers = ["http://localhost:5000"]

        self.server_url = servers[0].rstrip("/")
        self.server_urls = [s.rstrip("/") for s in servers]
        self.relay_url = relay_url.rstrip("/") if relay_url else (self.server_urls[1] if len(self.server_urls) > 1 else None)
        self.product_code = product_code
        self.heartbeat_interval = heartbeat_interval
        self.jitter_factor = jitter_factor
        self.timeout = timeout

        self._active_leases: Dict[str, threading.Event] = {}

    def acquire_seat(
        self,
        license_key: str,
        features: Optional[List[str]] = None,
        quantity: int = 1,
    ) -> SeatLease:
        """Acquires a floating concurrent seat from ControlPlane or Relay."""
        components = get_hardware_components()
        payload = {
            "licenseKey": license_key.strip(),
            "fingerprintComponents": components,
            "quantity": quantity,
            "features": features or [],
        }

        # Try server, fallback to relay
        url = f"{self.server_url}/v1/leases"
        data = self._post_json(url, payload)

        token = LeaseToken(
            lease_id=data["leaseId"],
            token_jwt=data.get("token", ""),
            seat_number=data.get("seat", 1),
            expires_at=datetime.fromisoformat(data["expiresAt"].replace("Z", "+00:00")),
            entitlements=data.get("entitlements", []),
        )

        lease = SeatLease(self, token, seq=data.get("seq", 0))
        self._start_heartbeat(lease, components)
        return lease

    def renew_seat(self, lease: Union[SeatLease, str], components: Dict[str, str]) -> dict:
        """Sends a heartbeat to renew the active lease with strict sequence monotonicity."""
        if isinstance(lease, SeatLease):
            lease_id = lease.lease_id
            seq = lease.seq
        else:
            lease_id = str(lease)
            seq = 0

        url = f"{self.server_url}/v1/leases/{lease_id}/renew"
        payload = {
            "clientSeq": seq,
            "fingerprintComponents": components,
        }
        res_data = self._post_json(url, payload)

        if isinstance(lease, SeatLease):
            if "leaseSeq" in res_data:
                lease.seq = res_data["leaseSeq"]
            new_jwt = res_data.get("token") or lease.token.token_jwt
            new_exp = (
                datetime.fromisoformat(res_data["expiresAt"].replace("Z", "+00:00"))
                if "expiresAt" in res_data
                else lease.token.expires_at
            )
            lease.token = LeaseToken(
                lease_id=lease.token.lease_id,
                token_jwt=new_jwt,
                seat_number=lease.token.seat_number,
                expires_at=new_exp,
                entitlements=lease.token.entitlements,
            )

        return res_data

    def release_seat(self, lease_id: str) -> None:
        """Frees the seat on the server."""
        url = f"{self.server_url}/v1/leases/{lease_id}"
        req = urllib.request.Request(
            url,
            headers={
                "User-Agent": "Symbolon-Python-SDK/1.0",
                "traceparent": _generate_w3c_traceparent(),
            },
            method="DELETE",
        )
        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as res:
                pass
        except Exception:
            pass  # Best effort release

    def borrow_seat(self, lease: Union[SeatLease, str], days: int) -> dict:
        """Borrows an active seat for offline roaming (1-30 days) and halts background heartbeats."""
        if days < 1 or days > 30:
            raise ValueError("Borrow duration must be between 1 and 30 days.")

        if isinstance(lease, SeatLease):
            lease_id = lease.lease_id
            self._stop_heartbeat(lease_id)
        else:
            lease_id = str(lease)
            self._stop_heartbeat(lease_id)

        url = f"{self.server_url}/v1/leases/{lease_id}/borrow"
        payload = {"days": days}
        res_data = self._post_json(url, payload)

        if isinstance(lease, SeatLease):
            lease._is_borrowed = True
            if "token" in res_data:
                borrowed_exp = (
                    datetime.fromisoformat(res_data["borrowedUntil"].replace("Z", "+00:00"))
                    if "borrowedUntil" in res_data
                    else lease.token.expires_at
                )
                lease.token = LeaseToken(
                    lease_id=lease.token.lease_id,
                    token_jwt=res_data["token"],
                    seat_number=lease.token.seat_number,
                    expires_at=borrowed_exp,
                    entitlements=lease.token.entitlements,
                )

        return res_data

    def return_borrowed_seat(self, lease_id: str) -> None:
        """Returns a borrowed offline seat early back to the floating pool."""
        self.release_seat(lease_id)

    def _start_heartbeat(self, lease: SeatLease, components: Dict[str, str]) -> None:
        stop_event = threading.Event()
        self._active_leases[lease.lease_id] = stop_event

        def worker():
            while not stop_event.is_set():
                # Apply ±jitter
                jitter = random.uniform(-self.jitter_factor, self.jitter_factor)
                sleep_time = max(1.0, self.heartbeat_interval * (1.0 + jitter))
                if stop_event.wait(sleep_time):
                    break
                try:
                    self.renew_seat(lease, components)
                except Exception:
                    pass

        thread = threading.Thread(target=worker, daemon=True, name=f"symbolon-hb-{lease.lease_id}")
        thread.start()

    def _stop_heartbeat(self, lease_id: str) -> None:
        if lease_id in self._active_leases:
            self._active_leases[lease_id].set()
            del self._active_leases[lease_id]

    def _post_json(self, url: str, data: dict) -> dict:
        body = json.dumps(data).encode("utf-8")
        req = urllib.request.Request(
            url,
            data=body,
            headers={
                "Content-Type": "application/json",
                "User-Agent": "Symbolon-Python-SDK/1.0",
                "traceparent": _generate_w3c_traceparent(),
            },
            method="POST",
        )

        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as res:
                return json.loads(res.read().decode("utf-8"))
        except urllib.error.HTTPError as e:
            if e.code == 409:
                raise SeatAllocationDenied("Floating capacity exhausted (0 seats available).")
            err_text = e.read().decode("utf-8", errors="ignore")
            raise SymbolonException(f"HTTP error {e.code}: {err_text}")
        except Exception as e:
            raise SymbolonException(f"Network error: {e}")

    def acquire_feature(
        self,
        lease_id: str,
        feature_code: str,
        version: Optional[str] = None,
        ttl_seconds: Optional[int] = None,
    ) -> dict:
        """Acquires a granular feature seat for an active lease."""
        url = f"{self.server_url}/v1/leases/{lease_id}/features/acquire"
        payload = {"featureCode": feature_code}
        if version:
            payload["version"] = version
        if ttl_seconds:
            payload["ttlSeconds"] = ttl_seconds
        try:
            return self._post_json(url, payload)
        except SeatAllocationDenied:
            raise FeatureDenied(f"Feature capacity exceeded for '{feature_code}'.")
        except SymbolonException as e:
            raise FeatureDenied(f"Failed to acquire feature '{feature_code}': {e}")

    def release_feature(self, lease_id: str, feature_code: str) -> bool:
        """Releases an acquired feature seat."""
        url = f"{self.server_url}/v1/leases/{lease_id}/features/release"
        payload = {"featureCode": feature_code}
        try:
            self._post_json(url, payload)
            return True
        except Exception:
            return False

    def get_features(self, lease_id: str) -> List[dict]:
        """Gets currently active features held by the lease."""
        url = f"{self.server_url}/v1/leases/{lease_id}/features"
        req = urllib.request.Request(
            url,
            headers={
                "User-Agent": "Symbolon-Python-SDK/1.0",
                "traceparent": _generate_w3c_traceparent(),
            },
            method="GET",
        )
        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as res:
                return json.loads(res.read().decode("utf-8"))
        except Exception:
            return []


def resolve_license_servers(input_str: Optional[str] = None, fallback_to_env: bool = True) -> List[str]:
    """
    Parses and resolves Symbolon license server URLs from a string or enterprise environment variables.
    Supports FlexNet port@host format (e.g., 27000@lic1.corp.com), comma/semicolon separated lists,
    and fallback to SYMBOLON_LICENSE_SERVER or SYMBOLON_SERVERS.
    """
    import os
    import re
    candidate = input_str
    if not candidate and fallback_to_env:
        candidate = os.environ.get("SYMBOLON_LICENSE_SERVER") or os.environ.get("SYMBOLON_SERVERS")
    if not candidate:
        return []

    tokens = [t.strip() for t in re.split(r"[;,]", candidate) if t.strip()]
    results = []
    flexnet_pattern = re.compile(r"^(?:(\d+))?@([^:]+)(?::(\d+))?$")

    for token in tokens:
        match = flexnet_pattern.match(token)
        if match:
            port = match.group(1) or match.group(3) or "8080"
            host = match.group(2)
            url = f"http://{host}:{port}"
            if url not in results:
                results.append(url)
            continue

        if token.startswith("http://") or token.startswith("https://"):
            url = token.rstrip("/")
            if url not in results:
                results.append(url)
        else:
            if ":" in token:
                url = f"http://{token}".rstrip("/")
            else:
                url = f"http://{token}:8080"
            if url not in results:
                results.append(url)

    return results


def discover_servers(timeout: float = 1.0, port: int = 7584, product_code: Optional[str] = None) -> List[dict]:
    """Discovers active Symbolon servers on the local subnet via UDP broadcast."""
    import socket
    discovered = []
    seen_urls = set()
    probe = {
        "magic": "symbolon:discover",
        "clientVersion": "1.0.0",
        "productCode": product_code,
        "clientId": socket.gethostname(),
        "timestamp": datetime.now(timezone.utc).isoformat(),
    }
    payload = json.dumps(probe).encode("utf-8")
    sock = socket.socket(socket.AF_INET, socket.SOCK_DGRAM, socket.IPPROTO_UDP)
    sock.setsockopt(socket.SOL_SOCKET, socket.SO_BROADCAST, 1)
    sock.settimeout(timeout)
    try:
        sock.sendto(payload, ("<broadcast>", port))
        start_time = time.time()
        while True:
            try:
                data, addr = sock.recvfrom(4096)
                rtt = (time.time() - start_time) * 1000.0
                packet = json.loads(data.decode("utf-8"))
                if packet.get("magic") == "symbolon:server":
                    url = packet.get("serverUrl")
                    if url and url not in seen_urls:
                        seen_urls.add(url)
                        packet["roundTripMs"] = round(rtt, 2)
                        packet["remoteAddress"] = addr[0]
                        discovered.append(packet)
            except socket.timeout:
                break
    finally:
        sock.close()
    return discovered


