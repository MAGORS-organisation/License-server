import os
import sys
import unittest

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))
from symbolon.client import SymbolonClient, TokenReservationScope
from symbolon.models import (
    TokenReservationDenied,
    ReserveTokensResponse,
    HeartbeatTokensResponse,
    CommitTokensResponse,
    RollbackTokensResponse,
    TokenWalletBalance,
)


class TestTokenClient(unittest.TestCase):

    def setUp(self):
        self.client = SymbolonClient("http://localhost:5000", product_code="test-suite")

    def test_reserve_tokens_success(self):
        posted = {}

        def mock_post(url, data):
            posted["url"] = url
            posted["data"] = data
            return {
                "success": True,
                "reservationId": "res_py_123",
                "reservedAmount": 150.0,
                "availableBalance": 850.0,
                "overdraftRemaining": 200.0,
            }

        self.client._post_json = mock_post
        res = self.client.reserve_tokens(
            wallet_id="wlt_py_1",
            feature_code="ml_compute",
            estimated_units=150.0,
            reservation_ttl_seconds=600,
            client_ref="job_42",
        )

        self.assertTrue(res.success)
        self.assertEqual(res.reservation_id, "res_py_123")
        self.assertEqual(res.reserved_amount, 150.0)
        self.assertEqual(res.available_balance, 850.0)
        self.assertTrue(posted["url"].endswith("/v1/tokens/reserve"))
        self.assertEqual(posted["data"]["walletId"], "wlt_py_1")
        self.assertEqual(posted["data"]["featureCode"], "ml_compute")
        self.assertEqual(posted["data"]["clientRef"], "job_42")
        self.assertEqual(posted["data"]["reservationTtl"], "00:10:00")

    def test_heartbeat_tokens_success(self):
        posted = {}

        def mock_post(url, data):
            posted["url"] = url
            posted["data"] = data
            return {
                "success": True,
                "totalConsumed": 50.0,
                "remainingReserved": 100.0,
                "availableBalance": 800.0,
            }

        self.client._post_json = mock_post
        res = self.client.heartbeat_tokens("res_py_123", delta_units=50.0)

        self.assertTrue(res.success)
        self.assertEqual(res.total_consumed, 50.0)
        self.assertEqual(res.remaining_reserved, 100.0)
        self.assertEqual(res.available_balance, 800.0)
        self.assertTrue(posted["url"].endswith("/v1/tokens/heartbeat"))

    def test_commit_tokens_success(self):
        posted = {}

        def mock_post(url, data):
            posted["url"] = url
            posted["data"] = data
            return {
                "success": True,
                "consumedCredits": 120.0,
                "refundedCredits": 30.0,
                "newBalance": 880.0,
            }

        self.client._post_json = mock_post
        res = self.client.commit_tokens("res_py_123", actual_units=120.0)

        self.assertTrue(res.success)
        self.assertEqual(res.consumed_credits, 120.0)
        self.assertEqual(res.refunded_credits, 30.0)
        self.assertEqual(res.new_balance, 880.0)
        self.assertTrue(posted["url"].endswith("/v1/tokens/commit"))

    def test_rollback_tokens_success(self):
        posted = {}

        def mock_post(url, data):
            posted["url"] = url
            posted["data"] = data
            return {
                "success": True,
                "restoredCredits": 150.0,
                "newBalance": 1000.0,
            }

        self.client._post_json = mock_post
        res = self.client.rollback_tokens("res_py_123", reason="Worker crashed")

        self.assertTrue(res.success)
        self.assertEqual(res.restored_credits, 150.0)
        self.assertEqual(res.new_balance, 1000.0)
        self.assertTrue(posted["url"].endswith("/v1/tokens/rollback"))
        self.assertEqual(posted["data"]["reason"], "Worker crashed")

    def test_get_token_wallet_balance_success(self):
        def mock_get(url):
            self.assertIn("/v1/tokens/wallets/wlt_py_1/balance", url)
            return {
                "walletId": "wlt_py_1",
                "walletCode": "WLT-CODE",
                "walletName": "Python Test Wallet",
                "totalCredits": 5000.0,
                "balance": 4000.0,
                "reservedCredits": 500.0,
                "availableBalance": 3500.0,
                "overdraftLimit": 500.0,
                "state": "Active",
                "isLowBalance": False,
            }

        self.client._get_json = mock_get
        balance = self.client.get_token_wallet_balance("wlt_py_1")

        self.assertEqual(balance.wallet_id, "wlt_py_1")
        self.assertEqual(balance.balance, 4000.0)
        self.assertEqual(balance.available_balance, 3500.0)
        self.assertEqual(balance.overdraft_limit, 500.0)
        self.assertFalse(balance.is_low_balance)

    def test_metered_scope_with_explicit_commit(self):
        calls = {"reserve": 0, "commit": 0, "rollback": 0}

        def mock_post(url, data):
            if url.endswith("/v1/tokens/reserve"):
                calls["reserve"] += 1
                return {
                    "success": True,
                    "reservationId": "res_scope_commit",
                    "reservedAmount": 100.0,
                    "availableBalance": 900.0,
                }
            if url.endswith("/v1/tokens/commit"):
                calls["commit"] += 1
                return {
                    "success": True,
                    "consumedCredits": 80.0,
                    "refundedCredits": 20.0,
                    "newBalance": 920.0,
                }
            if url.endswith("/v1/tokens/rollback"):
                calls["rollback"] += 1
                return {
                    "success": True,
                    "restoredCredits": 100.0,
                    "newBalance": 1000.0,
                }
            raise ValueError(f"Unexpected url: {url}")

        self.client._post_json = mock_post

        with self.client.metered_scope("wlt_1", "render", 100.0) as scope:
            self.assertEqual(scope.reservation_id, "res_scope_commit")
            self.assertFalse(scope.is_completed)
            commit_res = scope.commit(80.0)
            self.assertTrue(commit_res.success)
            self.assertTrue(scope.is_completed)
            self.assertEqual(scope.available_balance, 920.0)

        self.assertEqual(calls["reserve"], 1)
        self.assertEqual(calls["commit"], 1)
        self.assertEqual(calls["rollback"], 0, "explicit commit must prevent auto-rollback")

    def test_metered_scope_auto_rollback_on_exit(self):
        calls = {"reserve": 0, "rollback": 0}

        def mock_post(url, data):
            if url.endswith("/v1/tokens/reserve"):
                calls["reserve"] += 1
                return {
                    "success": True,
                    "reservationId": "res_scope_auto_rb",
                    "reservedAmount": 50.0,
                    "availableBalance": 950.0,
                }
            if url.endswith("/v1/tokens/rollback"):
                calls["rollback"] += 1
                return {
                    "success": True,
                    "restoredCredits": 50.0,
                    "newBalance": 1000.0,
                }
            raise ValueError(f"Unexpected url: {url}")

        self.client._post_json = mock_post

        with self.client.metered_scope("wlt_1", "nlp", 50.0) as scope:
            self.assertEqual(scope.reservation_id, "res_scope_auto_rb")
            # Exit block without calling scope.commit()

        self.assertEqual(calls["reserve"], 1)
        self.assertEqual(calls["rollback"], 1, "uncommitted scope must auto-rollback on context exit")

    def test_metered_scope_auto_rollback_on_exception(self):
        calls = {"reserve": 0, "rollback": 0}

        def mock_post(url, data):
            if url.endswith("/v1/tokens/reserve"):
                calls["reserve"] += 1
                return {
                    "success": True,
                    "reservationId": "res_scope_ex",
                    "reservedAmount": 50.0,
                    "availableBalance": 950.0,
                }
            if url.endswith("/v1/tokens/rollback"):
                calls["rollback"] += 1
                return {
                    "success": True,
                    "restoredCredits": 50.0,
                    "newBalance": 1000.0,
                }
            raise ValueError(f"Unexpected url: {url}")

        self.client._post_json = mock_post

        with self.assertRaises(RuntimeError):
            with self.client.metered_scope("wlt_1", "nlp", 50.0) as scope:
                raise RuntimeError("Something failed in business logic!")

        self.assertEqual(calls["reserve"], 1)
        self.assertEqual(calls["rollback"], 1, "exception in scope must trigger auto-rollback")

    def test_metered_scope_reservation_denied(self):
        def mock_post(url, data):
            return {
                "success": False,
                "failureReason": "Wallet suspended due to billing dispute",
            }

        self.client._post_json = mock_post

        with self.assertRaises(TokenReservationDenied) as ctx:
            with self.client.metered_scope("wlt_1", "nlp", 50.0):
                pass

        self.assertIn("Wallet suspended due to billing dispute", str(ctx.exception))


if __name__ == "__main__":
    unittest.main()
