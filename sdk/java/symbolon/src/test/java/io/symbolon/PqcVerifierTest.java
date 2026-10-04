package io.symbolon;

import io.symbolon.models.PqcAuditResult;
import org.junit.jupiter.api.DisplayName;
import org.junit.jupiter.api.Test;

import java.util.List;

import static org.junit.jupiter.api.Assertions.*;

class PqcVerifierTest {

    @Test
    @DisplayName("IsPostQuantum identifies NIST PQC algorithms correctly")
    void testIsPostQuantum() {
        assertTrue(PqcVerifier.isPostQuantum("ML-DSA-65"));
        assertTrue(PqcVerifier.isPostQuantum("ml-dsa-87"));
        assertTrue(PqcVerifier.isPostQuantum("ML-KEM-768"));
        assertTrue(PqcVerifier.isPostQuantum("SLH-DSA-SHA2-128s"));

        assertFalse(PqcVerifier.isPostQuantum("ES256"));
        assertFalse(PqcVerifier.isPostQuantum("RSA-2048"));
        assertFalse(PqcVerifier.isPostQuantum("Ed25519"));
        assertFalse(PqcVerifier.isPostQuantum(null));
    }

    @Test
    @DisplayName("IsCnsa2Compliant identifies CNSA 2.0 algorithms")
    void testIsCnsa2Compliant() {
        assertTrue(PqcVerifier.isCnsa2Compliant("ML-DSA-65"));
        assertTrue(PqcVerifier.isCnsa2Compliant("ML-KEM-1024"));
        assertFalse(PqcVerifier.isCnsa2Compliant("ML-DSA-44")); // Level 2, not CNSA 2.0
    }

    @Test
    @DisplayName("AuditAlgorithms approves hybrid scheme when strict is not required")
    void testHybridAudit() {
        PqcAuditResult result = PqcVerifier.auditAlgorithms(List.of("ES256", "ML-DSA-65"), false);
        assertTrue(result.isCompliant());
        assertFalse(result.isStrictPqc());
        assertTrue(result.missingAlgorithms().isEmpty());
    }

    @Test
    @DisplayName("AuditAlgorithms detects lack of PQC algorithms")
    void testVulnerableClassicalAudit() {
        PqcAuditResult result = PqcVerifier.auditAlgorithms(List.of("ES256"), false);
        assertFalse(result.isCompliant());
        assertTrue(result.missingAlgorithms().contains("ML-DSA-65"));
        assertTrue(result.recommendation().contains("Harvest-Now-Decrypt-Later"));
    }

    @Test
    @DisplayName("AuditAlgorithms validates pqc-strict profile")
    void testStrictPqcAudit() {
        PqcAuditResult result = PqcVerifier.auditAlgorithms(List.of("ML-DSA-65", "ML-KEM-768"), true);
        assertTrue(result.isCompliant());
        assertTrue(result.isStrictPqc());
    }
}
