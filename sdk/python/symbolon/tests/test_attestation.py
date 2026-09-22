import os
import sys
import unittest

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))

from symbolon.attestation import (
    EnclaveType,
    HardwareAttestationQuote,
    TpmQuoteGenerator,
    TpmQuoteVerifier,
)


class TestHardwareAttestation(unittest.TestCase):
    def setUp(self):
        self.aik_id = "aik-keystore-01"
        self.secret = "super-secret-aik-key-2026"
        self.nonce = "challenge_nonce_xyz123"

    def test_create_and_verify_valid_tpm_quote(self):
        quote = TpmQuoteGenerator.create_quote(
            aik_id=self.aik_id,
            secret_or_key=self.secret,
            nonce=self.nonce,
            pcr_indices=[0, 1, 7],
            enclave_type=EnclaveType.TPM_20,
        )

        self.assertIsNotNone(quote)
        self.assertEqual(quote.enclave_type, 1)
        self.assertEqual(quote.nonce, self.nonce)
        self.assertTrue(quote.pcr_digest.startswith("sha256:"))
        self.assertIn(0, quote.pcr_indices)
        self.assertIn(7, quote.pcr_indices)

        # Verification succeeds with matching nonce and key
        is_valid = TpmQuoteVerifier.verify_quote(
            quote=quote,
            expected_nonce=self.nonce,
            secret_or_key=self.secret,
            required_pcr_indices=[0, 7],
        )
        self.assertTrue(is_valid)

    def test_quote_roundtrip_dict_serialization(self):
        quote = TpmQuoteGenerator.create_quote(
            aik_id=self.aik_id,
            secret_or_key=self.secret,
            nonce=self.nonce,
            pcr_indices=[0, 1, 2, 7],
            enclave_type=EnclaveType.INTEL_SGX,
        )

        d = quote.to_dict()
        restored = HardwareAttestationQuote.from_dict(d)

        self.assertEqual(restored.enclave_type, 2)
        self.assertEqual(restored.aik_id, quote.aik_id)
        self.assertEqual(restored.pcr_digest, quote.pcr_digest)
        self.assertEqual(restored.signature, quote.signature)

    def test_verify_fails_on_nonce_mismatch_anti_replay(self):
        quote = TpmQuoteGenerator.create_quote(
            aik_id=self.aik_id,
            secret_or_key=self.secret,
            nonce=self.nonce,
        )

        # Nonce from previous session (replay attack)
        is_valid = TpmQuoteVerifier.verify_quote(
            quote=quote,
            expected_nonce="different_nonce_stale",
            secret_or_key=self.secret,
        )
        self.assertFalse(is_valid)

    def test_verify_fails_on_wrong_secret_or_tampered_quote(self):
        quote = TpmQuoteGenerator.create_quote(
            aik_id=self.aik_id,
            secret_or_key=self.secret,
            nonce=self.nonce,
        )

        # Wrong key
        self.assertFalse(
            TpmQuoteVerifier.verify_quote(quote, self.nonce, "wrong-key")
        )

        # Tampered quote data
        tampered = HardwareAttestationQuote(
            enclave_type=quote.enclave_type,
            aik_id=quote.aik_id,
            nonce=quote.nonce,
            pcr_indices=quote.pcr_indices,
            pcr_digest=quote.pcr_digest,
            quote_data=quote.quote_data + "_tampered",
            signature=quote.signature,
            signature_algorithm=quote.signature_algorithm,
            timestamp=quote.timestamp,
        )
        self.assertFalse(
            TpmQuoteVerifier.verify_quote(tampered, self.nonce, self.secret)
        )

    def test_verify_fails_on_missing_required_pcr(self):
        quote = TpmQuoteGenerator.create_quote(
            aik_id=self.aik_id,
            secret_or_key=self.secret,
            nonce=self.nonce,
            pcr_indices=[0, 1], # Missing PCR 7
        )

        # Requires PCR 7 (Secure Boot state)
        self.assertFalse(
            TpmQuoteVerifier.verify_quote(
                quote,
                self.nonce,
                self.secret,
                required_pcr_indices=[0, 7],
            )
        )


if __name__ == "__main__":
    unittest.main()
