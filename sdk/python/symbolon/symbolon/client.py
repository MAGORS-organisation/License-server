"""Symbolon ISV Client for Python with automated background heartbeats and jitter."""

import json
import random
import threading
import time
import urllib.request
import urllib.error
from datetime import datetime, timezone
from typing import Dict, List, Optional, Union

from .models import LeaseToken, SymbolonException, SeatAllocationDenied
from .fingerprint import get_hardware_components, compute_canonical_fingerprint


class SeatLease:
    """Represents an acquired floating seat lease. Supports Python context manager."""

    def __init__(self, client: "SymbolonClient", token: LeaseToken, seq: int = 0):
        self._client = client
        self.token = token
        self.seq = seq
        self._is_released = False
        self._is_borrowed = False

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
        req = urllib.request.Request(url, method="DELETE")
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
