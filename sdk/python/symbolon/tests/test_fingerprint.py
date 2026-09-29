import os
import tempfile
import unittest

from symbolon.fingerprint import (
    canonicalize,
    compute_canonical_fingerprint,
    evaluate_fingerprint_match,
    filter_valid_components,
    get_hardware_components,
    get_or_create_persisted_container_uuid,
    is_container_or_cloud,
    pseudonymize_host,
)


class TestFingerprint(unittest.TestCase):

    def test_canonicalize_and_hash_fpr4(self):
        comps = {
            "machineId": "ID-12345",
            "cpu": "Intel Core i7",
            "board": "ASUS PRIME",
        }
        can = canonicalize(comps)
        # Should be sorted alphabetically by uppercase key: BOARD, CPU, MACHINEID
        self.assertEqual(can, "BOARD=ASUS PRIME\nCPU=Intel Core i7\nMACHINEID=ID-12345\n")

        h = compute_canonical_fingerprint(comps)
        self.assertTrue(h.startswith("sha256:"))
        self.assertEqual(len(h), 7 + 64)

    def test_pseudonymize_host_fpr2(self):
        host = "Dev-Box-99.corp"
        salt = "lic_01JQ8ZK4N9V2X6M0"

        p1 = pseudonymize_host(host, salt)
        p2 = pseudonymize_host("dev-box-99.corp", salt)
        diff_salt = pseudonymize_host(host, "lic_other_salt")

        self.assertTrue(p1.startswith("sha256:"))
        self.assertEqual(p1, p2)
        self.assertNotEqual(p1, diff_salt)
        self.assertNotIn("Dev-Box", p1)

    def test_filter_placeholders_fpr3(self):
        raw = {
            "machineId": "VALID-ID",
            "cpu": "unknown",
            "board": "None",
            "disk": "00000000-0000-0000-0000-000000000000",
            "mac": "00:11:22:33:44:55",
            "empty": "   ",
        }
        filtered = filter_valid_components(raw)
        self.assertIn("machineId", filtered)
        self.assertIn("mac", filtered)
        self.assertNotIn("cpu", filtered)
        self.assertNotIn("board", filtered)
        self.assertNotIn("disk", filtered)
        self.assertNotIn("empty", filtered)

    def test_matching_strategies_fpr5(self):
        stored = {"machineId": "ID-1", "cpu": "CPU-1", "board": "BOARD-1"}

        # match-any
        incoming_any = {"machineId": "ID-DIFF", "cpu": "CPU-1", "board": "BOARD-DIFF"}
        r_any = evaluate_fingerprint_match(stored, incoming_any, strategy="match-any")
        self.assertTrue(r_any.is_match)
        self.assertEqual(r_any.matched_count, 1)

        # match-two
        r_two_fail = evaluate_fingerprint_match(stored, incoming_any, strategy="match-two")
        self.assertFalse(r_two_fail.is_match)

        incoming_two = {"machineId": "ID-1", "cpu": "CPU-1", "board": "BOARD-DIFF"}
        r_two_pass = evaluate_fingerprint_match(stored, incoming_two, strategy="match-two")
        self.assertTrue(r_two_pass.is_match)
        self.assertEqual(r_two_pass.matched_count, 2)

        # match-most (majority > 1.5)
        r_most_pass = evaluate_fingerprint_match(stored, incoming_two, strategy="match-most")
        self.assertTrue(r_most_pass.is_match)

        # match-all
        r_all_fail = evaluate_fingerprint_match(stored, incoming_two, strategy="match-all")
        self.assertFalse(r_all_fail.is_match)

        incoming_all = {"machineId": "ID-1", "cpu": "CPU-1", "board": "BOARD-1"}
        r_all_pass = evaluate_fingerprint_match(stored, incoming_all, strategy="match-all")
        self.assertTrue(r_all_pass.is_match)

    def test_match_most_degradation_fpr8(self):
        stored = {"machineId": "ID-1"}
        incoming_match = {"machineId": "ID-1"}
        incoming_fail = {"machineId": "ID-2"}

        # Common count = 1 (< 2): under FPR-8 match-most must degrade to match-all
        r_pass = evaluate_fingerprint_match(stored, incoming_match, strategy="match-most")
        self.assertTrue(r_pass.is_match)

        r_fail = evaluate_fingerprint_match(stored, incoming_fail, strategy="match-most")
        self.assertFalse(r_fail.is_match)
        self.assertIn("FPR-8", r_fail.failure_reason)

    def test_persisted_container_uuid_fpr12(self):
        with tempfile.TemporaryDirectory() as tmpdir:
            uuid_path = os.path.join(tmpdir, "test_uuid.txt")
            u1 = get_or_create_persisted_container_uuid(uuid_path)
            u2 = get_or_create_persisted_container_uuid(uuid_path)
            self.assertEqual(u1, u2)
            self.assertTrue(os.path.exists(uuid_path))


if __name__ == "__main__":
    unittest.main()
