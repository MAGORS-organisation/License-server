"""Data models for Symbolon Python SDK."""

from dataclasses import dataclass
from datetime import datetime
from typing import List, Optional


@dataclass(frozen=True)
class LeaseToken:
    lease_id: str
    token_jwt: str
    seat_number: int
    expires_at: datetime
    entitlements: List[str]


class SymbolonException(Exception):
    """Base exception for Symbolon SDK errors."""
    pass


class SeatAllocationDenied(SymbolonException):
    """Raised when floating pool has 0 available seats."""
    pass


class ClockSkewDetected(SymbolonException):
    """Raised when client and server clocks diverge beyond acceptable window."""
    pass
