"""Symbolon Air-Gap Delegated Seat Grant & Request (.symgrant, .symreq) Utilities.

Implements GNT-1..GNT-10, FLT-32..FLT-35 disjunctive seat allocation
and cryptographic PEM armor parsing.
"""

from __future__ import annotations

import base64
import json
import time
from dataclasses import dataclass
from typing import Any, Dict, Optional


def _base64url_decode(payload: str) -> bytes:
    """Decode URL-safe base64 string with optional missing padding."""
    rem = len(payload) % 4
    if rem > 0:
        payload += "=" * (4 - rem)
    return base64.urlsafe_b64decode(payload)


def _unwrap_pem(pem: str, header: str) -> bytes:
    """Unwrap a PEM-armored payload."""
    start_tag = f"-----BEGIN {header}-----"
    end_tag = f"-----END {header}-----"

    start_idx = pem.find(start_tag)
    end_idx = pem.find(end_tag)

    if start_idx == -1 or end_idx == -1 or end_idx <= start_idx:
        raise ValueError(f"Súbor neobsahuje platnú PEM obálku {header}")

    base64_str = pem[start_idx + len(start_tag):end_idx].strip()
    # Remove any whitespaces/newlines
    clean_base64 = "".join(base64_str.split())
    return base64.b64decode(clean_base64)


@dataclass(frozen=True)
class SeatGrant:
    """Represents a validated Symbolon Delegated Seat Grant (GNT-1..10)."""
    id: str
    license_id: str
    relay_id: str
    seats: int
    seat_from: int
    seat_to: int
    seq: int
    supersedes: Optional[int]
    not_before: int
    not_after: int
    ephemeral_key: Optional[Dict[str, Any]] = None

    @property
    def is_disjunctive_valid(self) -> bool:
        """Enforces GNT-3 disjunctive contiguous seat count invariant."""
        return (self.seat_to - self.seat_from + 1) == self.seats and self.seat_from >= 1

    def contains_seat(self, seat_no: int) -> bool:
        """Enforces GNT-10 seat membership boundary check."""
        return self.seat_from <= seat_no <= self.seat_to

    def is_active(self, now: Optional[int] = None) -> bool:
        """Check if grant is active according to time window."""
        t = now if now is not None else int(time.time())
        return self.not_before <= t <= self.not_after


@dataclass(frozen=True)
class AirGapRequest:
    """Represents a validated Symbolon Air-Gap Capacity Request (.symreq, FLT-32..35)."""
    relay_id: str
    license_key: str
    requested_seats: int
    last_seq: int
    usage_digest: str
    nonce: str
    exp: int

    def is_expired(self, now: Optional[int] = None) -> bool:
        """Check if request has expired."""
        t = now if now is not None else int(time.time())
        return t > self.exp


def parse_symgrant(pem: str) -> SeatGrant:
    """Parse a .symgrant PEM document and extract the SeatGrant claims."""
    raw_json_bytes = _unwrap_pem(pem, "SYMBOLON SEAT GRANT")
    doc = json.loads(raw_json_bytes.decode("utf-8"))

    if "payload" not in doc:
        raise ValueError("Neplatný formát JWS: chýba atribút payload")

    payload_bytes = _base64url_decode(doc["payload"])
    claims = json.loads(payload_bytes.decode("utf-8"))

    symgrant = claims.get("symgrant")
    if not symgrant:
        raise ValueError("Chýbajúca symgrant sekcia v claimoch")

    seat_range = symgrant.get("seatRange", [0, 0])
    seat_from = seat_range[0] if len(seat_range) > 0 else 0
    seat_to = seat_range[1] if len(seat_range) > 1 else 0

    return SeatGrant(
        id=claims.get("jti", ""),
        license_id=claims.get("sub", ""),
        relay_id=claims.get("aud", ""),
        seats=symgrant.get("seats", 0),
        seat_from=seat_from,
        seat_to=seat_to,
        seq=symgrant.get("seq", 0),
        supersedes=symgrant.get("supersedes"),
        not_before=claims.get("nbf", 0),
        not_after=claims.get("exp", 0),
        ephemeral_key=symgrant.get("ephemeralKey")
    )


def parse_symreq(pem: str) -> AirGapRequest:
    """Parse a .symreq PEM document and extract the AirGapRequest claims."""
    raw_json_bytes = _unwrap_pem(pem, "SYMBOLON GRANT REQUEST")
    doc = json.loads(raw_json_bytes.decode("utf-8"))

    if "payload" not in doc:
        raise ValueError("Neplatný formát JWS: chýba atribút payload")

    payload_bytes = _base64url_decode(doc["payload"])
    claims = json.loads(payload_bytes.decode("utf-8"))

    symreq = claims.get("symreq")
    if not symreq:
        raise ValueError("Chýbajúca symreq sekcia v claimoch")

    return AirGapRequest(
        relay_id=claims.get("iss", symreq.get("relayId", "")),
        license_key=claims.get("sub", symreq.get("licenseKey", "")),
        requested_seats=symreq.get("requestedSeats", 0),
        last_seq=symreq.get("lastSeq", 0),
        usage_digest=symreq.get("usageDigest", ""),
        nonce=symreq.get("nonce", ""),
        exp=claims.get("exp", 0)
    )
