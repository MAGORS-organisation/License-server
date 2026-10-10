import base64
import json
import os
import sys
import unittest

sys.path.insert(0, os.path.abspath(os.path.join(os.path.dirname(__file__), "..")))
from symbolon.grant import SeatGrant, AirGapRequest, parse_symgrant, parse_symreq


def _b64url_encode(data: bytes) -> str:
    return base64.urlsafe_b64encode(data).decode("utf-8").rstrip("=")


def _make_jws_pem(header_type: str, payload_obj: dict) -> str:
    payload_json = json.dumps(payload_obj).encode("utf-8")
    b64_payload = _b64url_encode(payload_json)

    doc = {
        "payload": b64_payload,
        "signatures": [
            {
                "protected": _b64url_encode(json.dumps({"alg": "ES256", "kid": "key-test"}).encode("utf-8")),
                "signature": _b64url_encode(b"dummy_sig_bytes")
            }
        ]
    }

    doc_json = json.dumps(doc).encode("utf-8")
    doc_b64 = base64.b64encode(doc_json).decode("utf-8")

    # Format into 64-char lines
    chunks = [doc_b64[i:i+64] for i in range(0, len(doc_b64), 64)]
    body = "\n".join(chunks)

    return f"-----BEGIN {header_type}-----\n{body}\n-----END {header_type}-----"


class TestSeatGrantAndAirGap(unittest.TestCase):

    def test_parse_valid_symgrant_and_verify_invariants(self):
        payload = {
            "iss": "https://symbolon.enterprise.local",
            "sub": "SYM1-TEST-KEY",
            "aud": "rly_plant_01",
            "jti": "gnt_123456",
            "nbf": 1000,
            "exp": 5000,
            "symgrant": {
                "v": 1,
                "relayId": "rly_plant_01",
                "licenseKey": "SYM1-TEST-KEY",
                "seats": 5,
                "seatRange": [11, 15],
                "seq": 2,
                "supersedes": 1,
                "ephemeralKey": {
                    "kty": "EC",
                    "crv": "P-256",
                    "x": "abc",
                    "y": "def"
                }
            }
        }

        pem = _make_jws_pem("SYMBOLON SEAT GRANT", payload)
        grant = parse_symgrant(pem)

        self.assertEqual(grant.id, "gnt_123456")
        self.assertEqual(grant.license_id, "SYM1-TEST-KEY")
        self.assertEqual(grant.relay_id, "rly_plant_01")
        self.assertEqual(grant.seats, 5)
        self.assertEqual(grant.seat_from, 11)
        self.assertEqual(grant.seat_to, 15)
        self.assertEqual(grant.seq, 2)
        self.assertEqual(grant.supersedes, 1)

        # Invariant GNT-3: (seat_to - seat_from + 1) == seats
        self.assertTrue(grant.is_disjunctive_valid)

        # Invariant GNT-10: seat range check
        self.assertTrue(grant.contains_seat(11))
        self.assertTrue(grant.contains_seat(13))
        self.assertTrue(grant.contains_seat(15))
        self.assertFalse(grant.contains_seat(10))
        self.assertFalse(grant.contains_seat(16))

        # Time window check
        self.assertTrue(grant.is_active(3000))
        self.assertFalse(grant.is_active(500))
        self.assertFalse(grant.is_active(6000))

        # Ephemeral key present
        self.assertIsNotNone(grant.ephemeral_key)
        self.assertEqual(grant.ephemeral_key.get("kty"), "EC")

    def test_parse_symgrant_with_invalid_range_fails_disjunction(self):
        payload = {
            "iss": "https://symbolon.enterprise.local",
            "sub": "SYM1-TEST-KEY",
            "aud": "rly_plant_01",
            "jti": "gnt_invalid",
            "nbf": 1000,
            "exp": 5000,
            "symgrant": {
                "seats": 5,
                "seatRange": [1, 4]  # only 4 seats, but seats claim is 5!
            }
        }

        pem = _make_jws_pem("SYMBOLON SEAT GRANT", payload)
        grant = parse_symgrant(pem)
        self.assertFalse(grant.is_disjunctive_valid)

    def test_parse_valid_symreq(self):
        payload = {
            "iss": "rly_plant_02",
            "sub": "SYM1-PROD-KEY",
            "jti": "req_98765",
            "exp": 8000,
            "symreq": {
                "v": 1,
                "relayId": "rly_plant_02",
                "licenseKey": "SYM1-PROD-KEY",
                "requestedSeats": 8,
                "lastSeq": 3,
                "usageDigest": "sha256:abc123def456",
                "nonce": "nonce_random_xyz"
            }
        }

        pem = _make_jws_pem("SYMBOLON GRANT REQUEST", payload)
        req = parse_symreq(pem)

        self.assertEqual(req.relay_id, "rly_plant_02")
        self.assertEqual(req.license_key, "SYM1-PROD-KEY")
        self.assertEqual(req.requested_seats, 8)
        self.assertEqual(req.last_seq, 3)
        self.assertEqual(req.usage_digest, "sha256:abc123def456")
        self.assertEqual(req.nonce, "nonce_random_xyz")
        self.assertFalse(req.is_expired(7000))
        self.assertTrue(req.is_expired(9000))

    def test_invalid_pem_header_raises_value_error(self):
        with self.assertRaises(ValueError):
            parse_symgrant("not a pem file")

        with self.assertRaises(ValueError):
            parse_symreq("-----BEGIN SOMETHING ELSE-----\nabc\n-----END SOMETHING ELSE-----")


if __name__ == "__main__":
    unittest.main()
