import os
import sys
import unittest
from unittest.mock import MagicMock
from datetime import datetime, timezone

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))
from symbolon.models import LeaseToken
from symbolon.client import SymbolonClient, SeatLease


class TestBorrowingAndRoaming(unittest.TestCase):

    def test_borrow_seat_sends_correct_payload_and_updates_token(self):
        client = SymbolonClient("http://localhost:5000", "test-product")
        token = LeaseToken(
            lease_id="lse_test_borrow",
            token_jwt="jwt.online",
            seat_number=1,
            expires_at=datetime.now(timezone.utc),
            entitlements=["core"]
        )
        lease = SeatLease(client, token, seq=0)

        # Mock heartbeat active event
        client._active_leases["lse_test_borrow"] = MagicMock()

        recorded_calls = []
        def mock_post_json(url, payload):
            recorded_calls.append((url, payload))
            return {
                "leaseId": "lse_test_borrow",
                "borrowedUntil": "2026-10-10T12:00:00Z",
                "token": "jwt.offline_roaming_token"
            }

        client._post_json = mock_post_json

        res = lease.borrow(14)

        # Verification
        self.assertEqual(len(recorded_calls), 1)
        self.assertEqual(recorded_calls[0][0], "http://localhost:5000/v1/leases/lse_test_borrow/borrow")
        self.assertEqual(recorded_calls[0][1], {"days": 14})
        self.assertTrue(lease.is_borrowed)
        self.assertEqual(lease.token.token_jwt, "jwt.offline_roaming_token")
        self.assertNotIn("lse_test_borrow", client._active_leases)

    def test_borrow_invalid_days_raises_value_error(self):
        client = SymbolonClient("http://localhost:5000", "test-product")
        with self.assertRaises(ValueError):
            client.borrow_seat("lse_1", 0)
        with self.assertRaises(ValueError):
            client.borrow_seat("lse_1", 31)

    def test_return_borrowed_seat_calls_release(self):
        client = SymbolonClient("http://localhost:5000", "test-product")
        released_ids = []
        client.release_seat = lambda lease_id: released_ids.append(lease_id)

        client.return_borrowed_seat("lse_returned_123")
        self.assertEqual(released_ids, ["lse_returned_123"])

    def test_context_manager_exit_does_not_release_borrowed_seat(self):
        client = SymbolonClient("http://localhost:5000", "test-product")
        released_ids = []
        client.release_seat = lambda lease_id: released_ids.append(lease_id)

        token = LeaseToken(
            lease_id="lse_cm_test",
            token_jwt="jwt.online",
            seat_number=1,
            expires_at=datetime.now(timezone.utc),
            entitlements=["core"]
        )

        def mock_post_json(url, payload):
            return {
                "leaseId": "lse_cm_test",
                "borrowedUntil": "2026-10-10T12:00:00Z",
                "token": "jwt.offline_token"
            }
        client._post_json = mock_post_json

        with SeatLease(client, token) as lease:
            lease.borrow(7)

        # Context manager exit must NOT release borrowed roaming seat
        self.assertEqual(len(released_ids), 0)


if __name__ == "__main__":
    unittest.main()
