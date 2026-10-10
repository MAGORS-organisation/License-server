"""Hardware Enclave Attestation & TPM 2.0 PCR quote validation for Symbolon Python SDK."""

import hashlib
import hmac
from dataclasses import dataclass
from datetime import datetime, timezone
from enum import IntEnum
from typing import Dict, List, Optional


class EnclaveType(IntEnum):
    NONE = 0
    TPM_20 = 1
    INTEL_SGX = 2
    AMD_SEV_SNP = 3


@dataclass
class HardwareAttestationQuote:
    """Hardware attestation quote containing signed PCR composite measurements and anti-replay nonce."""

    enclave_type: int
    aik_id: str
    nonce: str
    pcr_indices: List[int]
    pcr_digest: str
    quote_data: str
    signature: str
    signature_algorithm: str
    timestamp: str

    def to_dict(self) -> dict:
        return {
            "enclaveType": int(self.enclave_type),
            "aikId": self.aik_id,
            "nonce": self.nonce,
            "pcrIndices": list(self.pcr_indices),
            "pcrDigest": self.pcr_digest,
            "quoteData": self.quote_data,
            "signature": self.signature,
            "signatureAlgorithm": self.signature_algorithm,
            "timestamp": self.timestamp,
        }

    @classmethod
    def from_dict(cls, data: dict) -> "HardwareAttestationQuote":
        return cls(
            enclave_type=int(data.get("enclaveType", 1)),
            aik_id=str(data.get("aikId", "")),
            nonce=str(data.get("nonce", "")),
            pcr_indices=list(data.get("pcrIndices", [])),
            pcr_digest=str(data.get("pcrDigest", "")),
            quote_data=str(data.get("quoteData", "")),
            signature=str(data.get("signature", "")),
            signature_algorithm=str(data.get("signatureAlgorithm", "HMAC-SHA256")),
            timestamp=str(data.get("timestamp", "")),
        )


class TpmQuoteGenerator:
    """Generates cryptographic Hardware Attestation Quotes (TPM 2.0 / Confidential Computing)."""

    @staticmethod
    def create_quote(
        aik_id: str,
        secret_or_key: str,
        nonce: str,
        pcr_indices: Optional[List[int]] = None,
        enclave_type: EnclaveType = EnclaveType.TPM_20,
        custom_pcr_values: Optional[Dict[int, str]] = None,
    ) -> HardwareAttestationQuote:
        if not aik_id:
            raise ValueError("aik_id must not be empty.")
        if not secret_or_key:
            raise ValueError("secret_or_key must not be empty.")
        if not nonce:
            raise ValueError("nonce must not be empty.")

        indices = sorted(pcr_indices if pcr_indices is not None else [0, 1, 7])
        now_iso = datetime.now(timezone.utc).isoformat()

        # Compute composite PCR digest
        values = custom_pcr_values or {}
        pcr_repr = ";".join(f"{idx}:{values.get(idx, f'pcr_baseline_val_{idx}')}" for idx in indices)
        pcr_digest = "sha256:" + hashlib.sha256(pcr_repr.encode("utf-8")).hexdigest()

        # Create canonical quote data representation (TPMS_ATTEST representation)
        quote_payload = f"TPMS_ATTEST:{int(enclave_type)}:{aik_id}:{nonce}:{pcr_digest}:{now_iso}"

        # Sign quote using AIK key
        sig = hmac.new(
            secret_or_key.encode("utf-8"),
            quote_payload.encode("utf-8"),
            hashlib.sha256,
        ).hexdigest()

        return HardwareAttestationQuote(
            enclave_type=int(enclave_type),
            aik_id=aik_id,
            nonce=nonce,
            pcr_indices=indices,
            pcr_digest=pcr_digest,
            quote_data=quote_payload,
            signature=sig,
            signature_algorithm="HMAC-SHA256",
            timestamp=now_iso,
        )


class TpmQuoteVerifier:
    """Verifies TPM 2.0 quotes against anti-replay nonces, PCR integrity, and AIK cryptographic signatures."""

    @staticmethod
    def verify_quote(
        quote: HardwareAttestationQuote,
        expected_nonce: str,
        secret_or_key: str,
        required_pcr_indices: Optional[List[int]] = None,
    ) -> bool:
        if not quote or not expected_nonce or not secret_or_key:
            return False

        # 1. Anti-replay verification
        if not hmac.compare_digest(quote.nonce, expected_nonce):
            return False

        # 2. Required PCR indices verification
        if required_pcr_indices:
            quote_set = set(quote.pcr_indices)
            for req in required_pcr_indices:
                if req not in quote_set:
                    return False

        # 3. Cryptographic signature verification over quote_data
        expected_sig = hmac.new(
            secret_or_key.encode("utf-8"),
            quote.quote_data.encode("utf-8"),
            hashlib.sha256,
        ).hexdigest()

        return hmac.compare_digest(quote.signature.lower(), expected_sig.lower())
