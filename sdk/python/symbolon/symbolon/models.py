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


class FeatureDenied(SymbolonException):
    """Raised when feature entitlement or concurrency limit is denied."""
    pass


class TokenReservationDenied(SymbolonException):
    """Raised when token wallet balance is insufficient or wallet is inactive."""
    pass


@dataclass(frozen=True)
class ReserveTokensResponse:
    success: bool
    reservation_id: Optional[str]
    reserved_amount: float
    available_balance: float
    overdraft_remaining: float = 0.0
    failure_reason: Optional[str] = None


@dataclass(frozen=True)
class HeartbeatTokensResponse:
    success: bool
    total_consumed: float
    remaining_reserved: float
    available_balance: float
    failure_reason: Optional[str] = None


@dataclass(frozen=True)
class CommitTokensResponse:
    success: bool
    consumed_credits: float
    refunded_credits: float
    new_balance: float
    failure_reason: Optional[str] = None


@dataclass(frozen=True)
class RollbackTokensResponse:
    success: bool
    restored_credits: float
    new_balance: float
    failure_reason: Optional[str] = None


@dataclass(frozen=True)
class TokenWalletBalance:
    wallet_id: str
    wallet_code: str
    wallet_name: str
    total_credits: float
    balance: float
    reserved_credits: float
    available_balance: float
    overdraft_limit: float
    state: str
    is_low_balance: bool
    expires_at: Optional[str] = None


