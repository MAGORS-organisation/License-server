"""Symbolon ISV Client for Python with automated background heartbeats and jitter."""

import json
import random
import threading
import time
import urllib.request
import urllib.error
from datetime import datetime, timezone
from typing import Dict, List, Optional

from .models import LeaseToken, SymbolonException, SeatAllocationDenied
from .fingerprint import get_hardware_components, compute_canonical_fingerprint


class SeatLease:
    """Represents an acquired floating seat lease. Supports Python context manager."""

    def __init__(self, client: "SymbolonClient", token: LeaseToken):
        self._client = client
        self.token = token
        self._is_released = False

    @property
    def lease_id(self) -> str:
        return self.token.lease_id

    @property
    def seat_number(self) -> int:
        return self.token.seat_number

    @property
    def is_active(self) -> bool:
        return not self._is_released

    def release(self) -> None:
        """Explicitly releases the allocated floating seat back to the pool."""
        if not self._is_released:
            self._client._stop_heartbeat(self.lease_id)
            self._client.release_seat(self.lease_id)
            self._is_released = True

    def __enter__(self) -> "SeatLease":
        return self

    def __exit__(self, exc_type, exc_val, exc_tb) -> None:
        self.release()


class SymbolonClient:
    """Official Symbolon Client SDK for Python applications."""

    def __init__(
        self,
        server_url: str,
        product_code: str,
        relay_url: Optional[str] = None,
        heartbeat_interval: float = 30.0,
        jitter_factor: float = 0.10,
        timeout: float = 10.0,
    ):
        self.server_url = server_url.rstrip("/")
        self.relay_url = relay_url.rstrip("/") if relay_url else None
        self.product_code = product_code
        self.heartbeat_interval = heartbeat_interval
        self.jitter_factor = jitter_factor
        self.timeout = timeout

        self._active_leases: Dict[str, threading.Event] = {}
        self._seq_counter = 1
        self._lock = threading.Lock()

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

        lease = SeatLease(self, token)
        self._start_heartbeat(token.lease_id, components)
        return lease

    def renew_seat(self, lease_id: str, components: Dict[str, str]) -> None:
        """Sends a heartbeat to renew the active lease."""
        with self._lock:
            self._seq_counter += 1
            seq = self._seq_counter

        url = f"{self.server_url}/v1/leases/{lease_id}/renew"
        payload = {
            "clientSeq": seq,
            "fingerprintComponents": components,
        }
        self._post_json(url, payload)

    def release_seat(self, lease_id: str) -> None:
        """Frees the seat on the server."""
        url = f"{self.server_url}/v1/leases/{lease_id}"
        req = urllib.request.Request(url, method="DELETE")
        try:
            with urllib.request.urlopen(req, timeout=self.timeout) as res:
                pass
        except Exception:
            pass  # Best effort release

    def _start_heartbeat(self, lease_id: str, components: Dict[str, str]) -> None:
        stop_event = threading.Event()
        self._active_leases[lease_id] = stop_event

        def worker():
            while not stop_event.is_set():
                # Apply ±jitter
                jitter = random.uniform(-self.jitter_factor, self.jitter_factor)
                sleep_time = max(1.0, self.heartbeat_interval * (1.0 + jitter))
                if stop_event.wait(sleep_time):
                    break
                try:
                    self.renew_seat(lease_id, components)
                except Exception:
                    pass

        thread = threading.Thread(target=worker, daemon=True, name=f"symbolon-hb-{lease_id}")
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
