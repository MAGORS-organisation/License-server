import os
import sys
import unittest

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))
from symbolon.fingerprint import get_hardware_components, compute_canonical_fingerprint, get_local_fingerprint


class TestSymbolonPythonSdk(unittest.TestCase):

    def test_fingerprint_generation_is_canonical_and_deterministic(self):
        components_a = {"os": "linux", "arch": "x86_64", "hostname": "worker1"}
        components_b = {"hostname": "worker1", "os": "linux", "arch": "x86_64"}

        # Key ordering must produce identical SHA-256 hash
        hash_a = compute_canonical_fingerprint(components_a)
        hash_b = compute_canonical_fingerprint(components_b)

        self.assertTrue(hash_a.startswith("sha256:"))
        self.assertEqual(hash_a, hash_b)
        self.assertEqual(len(hash_a), 7 + 64)

    def test_local_fingerprint_succeeds(self):
        fp = get_local_fingerprint()
        self.assertTrue(fp.startswith("sha256:"))
        self.assertEqual(len(fp), 71)

    def test_renew_seat_preserves_monotonic_sequence(self):
        from datetime import datetime, timezone
        from symbolon.models import LeaseToken
        from symbolon.client import SymbolonClient, SeatLease

        client = SymbolonClient("http://localhost:5000", "test-product")
        token = LeaseToken(
            lease_id="lse_test_seq",
            token_jwt="jwt.token",
            seat_number=1,
            expires_at=datetime.now(timezone.utc),
            entitlements=["core"]
        )
        lease = SeatLease(client, token, seq=0)
        self.assertEqual(lease.seq, 0)

        # Mock _post_json to simulate server responding with leaseSeq: 1
        recorded_payloads = []
        def mock_post_json(url, payload):
            recorded_payloads.append(payload)
            return {
                "token": "jwt.renewed",
                "expiresAt": "2026-12-31T23:59:59Z",
                "leaseSeq": payload["clientSeq"] + 1
            }

        client._post_json = mock_post_json

        # First renewal: sends seq 0, server responds with leaseSeq 1
        client.renew_seat(lease, {"os": "windows"})
        self.assertEqual(recorded_payloads[0]["clientSeq"], 0)
        self.assertEqual(lease.seq, 1)

        # Second renewal: sends seq 1, server responds with leaseSeq 2
        client.renew_seat(lease, {"os": "windows"})
        self.assertEqual(recorded_payloads[1]["clientSeq"], 1)
        self.assertEqual(lease.seq, 2)


if __name__ == "__main__":
    unittest.main()
