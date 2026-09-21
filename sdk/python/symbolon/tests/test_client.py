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


if __name__ == "__main__":
    unittest.main()
