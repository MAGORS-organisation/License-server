"""Unit tests for Symbolon Post-Quantum Era Suite in Python SDK (Phase 25 / Míľnik M7, §13.5)."""

import unittest
from symbolon.pqc import (
    PqcAlgorithm,
    PqcProfile,
    calculate_pqc_readiness,
    is_algorithm_permitted,
    is_cnsa2_compliant,
    is_post_quantum,
)


class TestPqcSuite(unittest.TestCase):
    def test_is_post_quantum(self):
        """Verifies identification of FIPS 203, 204, and 205 algorithms."""
        self.assertTrue(is_post_quantum(PqcAlgorithm.ML_DSA_65))
        self.assertTrue(is_post_quantum(PqcAlgorithm.ML_DSA_87))
        self.assertTrue(is_post_quantum(PqcAlgorithm.ML_KEM_768))
        self.assertTrue(is_post_quantum(PqcAlgorithm.ML_KEM_1024))
        self.assertTrue(is_post_quantum(PqcAlgorithm.SLH_DSA_SHA2_128S))

        # Classical algorithms are not quantum-safe
        self.assertFalse(is_post_quantum(PqcAlgorithm.ES256))
        self.assertFalse(is_post_quantum("RS256"))
        self.assertFalse(is_post_quantum("RSA4096"))

    def test_is_cnsa2_compliant(self):
        """Verifies adherence to US Commercial National Security Algorithm Suite 2.0."""
        self.assertTrue(is_cnsa2_compliant(PqcAlgorithm.ML_DSA_65))
        self.assertTrue(is_cnsa2_compliant(PqcAlgorithm.ML_DSA_87))
        self.assertTrue(is_cnsa2_compliant(PqcAlgorithm.ML_KEM_768))
        self.assertTrue(is_cnsa2_compliant(PqcAlgorithm.ML_KEM_1024))
        self.assertTrue(is_cnsa2_compliant(PqcAlgorithm.SLH_DSA_SHA2_128S))

        # Smaller parameter sets are not approved for CNSA 2.0
        self.assertFalse(is_cnsa2_compliant(PqcAlgorithm.ML_DSA_44))
        self.assertFalse(is_cnsa2_compliant(PqcAlgorithm.ML_KEM_512))
        self.assertFalse(is_cnsa2_compliant(PqcAlgorithm.ES256))

    def test_profile_enforcement(self):
        """Verifies algorithm filtering across hybrid-v1 and pqc-strict profiles."""
        # hybrid-v1 permits both classical and PQC
        self.assertTrue(is_algorithm_permitted(PqcAlgorithm.ES256, PqcProfile.HYBRID_V1))
        self.assertTrue(is_algorithm_permitted(PqcAlgorithm.ML_DSA_65, PqcProfile.HYBRID_V1))

        # pqc-strict rejects all classical algorithms (Zero Classical Cryptography)
        self.assertFalse(is_algorithm_permitted(PqcAlgorithm.ES256, PqcProfile.PQC_STRICT))
        self.assertTrue(is_algorithm_permitted(PqcAlgorithm.ML_DSA_65, PqcProfile.PQC_STRICT))
        self.assertTrue(is_algorithm_permitted(PqcAlgorithm.ML_KEM_768, PqcProfile.PQC_STRICT))

    def test_readiness_audit_classical(self):
        """Tests quantum vulnerability audit on classical infrastructure."""
        keys = [{"kid": "k1", "alg": PqcAlgorithm.ES256}]
        licenses = [{"id": "lic1", "alg": PqcAlgorithm.ES256}]

        audit = calculate_pqc_readiness(keys, licenses, active_profile=PqcProfile.HYBRID_V1)
        self.assertLess(audit.readiness_score, 50.0)
        self.assertEqual(audit.classical_keys, 1)
        self.assertEqual(audit.pqc_keys, 0)
        self.assertEqual(audit.at_risk_licenses, 1)
        self.assertFalse(audit.is_cnsa2_ready)
        self.assertTrue(len(audit.action_items) > 0)

    def test_readiness_audit_pure_pqc(self):
        """Tests quantum readiness audit on fully migrated PQC infrastructure."""
        keys = [
            {"kid": "k1", "alg": PqcAlgorithm.ML_DSA_65},
            {"kid": "k2", "alg": PqcAlgorithm.ML_KEM_768},
        ]
        licenses = [
            {"id": "lic1", "alg": PqcAlgorithm.ML_DSA_65},
        ]

        audit = calculate_pqc_readiness(keys, licenses, active_profile=PqcProfile.PQC_STRICT)
        self.assertEqual(audit.readiness_score, 100.0)
        self.assertEqual(audit.classical_keys, 0)
        self.assertEqual(audit.pqc_keys, 2)
        self.assertEqual(audit.pqc_licenses, 1)
        self.assertEqual(audit.at_risk_licenses, 0)
        self.assertTrue(audit.is_cnsa2_ready)
        self.assertTrue(audit.is_nis2_ready)
        self.assertIn("EXCELLENT", audit.summary)


if __name__ == "__main__":
    unittest.main()
