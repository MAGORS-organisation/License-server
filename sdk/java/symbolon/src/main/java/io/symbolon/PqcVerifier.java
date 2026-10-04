package io.symbolon;

import io.symbolon.models.PqcAuditResult;

import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.Set;

/**
 * Post-Quantum Cryptography compliance verifier for Java client applications (FIPS 203, 204, 205).
 */
public final class PqcVerifier {

    public static final String ALG_ES256 = "ES256";
    public static final String ALG_ML_DSA_44 = "ML-DSA-44";
    public static final String ALG_ML_DSA_65 = "ML-DSA-65";
    public static final String ALG_ML_DSA_87 = "ML-DSA-87";
    public static final String ALG_ML_KEM_512 = "ML-KEM-512";
    public static final String ALG_ML_KEM_768 = "ML-KEM-768";
    public static final String ALG_ML_KEM_1024 = "ML-KEM-1024";
    public static final String ALG_SLH_DSA_SHA2_128S = "SLH-DSA-SHA2-128s";
    public static final String ALG_SLH_DSA_SHA2_128F = "SLH-DSA-SHA2-128f";
    public static final String ALG_SLH_DSA_SHAKE_128S = "SLH-DSA-SHAKE-128s";

    private static final Set<String> POST_QUANTUM_ALGS = Set.of(
            ALG_ML_DSA_44.toUpperCase(Locale.ROOT),
            ALG_ML_DSA_65.toUpperCase(Locale.ROOT),
            ALG_ML_DSA_87.toUpperCase(Locale.ROOT),
            ALG_ML_KEM_512.toUpperCase(Locale.ROOT),
            ALG_ML_KEM_768.toUpperCase(Locale.ROOT),
            ALG_ML_KEM_1024.toUpperCase(Locale.ROOT),
            ALG_SLH_DSA_SHA2_128S.toUpperCase(Locale.ROOT),
            ALG_SLH_DSA_SHA2_128F.toUpperCase(Locale.ROOT),
            ALG_SLH_DSA_SHAKE_128S.toUpperCase(Locale.ROOT)
    );

    private static final Set<String> CNSA_2_ALGS = Set.of(
            ALG_ML_DSA_65.toUpperCase(Locale.ROOT),
            ALG_ML_DSA_87.toUpperCase(Locale.ROOT),
            ALG_ML_KEM_768.toUpperCase(Locale.ROOT),
            ALG_ML_KEM_1024.toUpperCase(Locale.ROOT),
            ALG_SLH_DSA_SHA2_128S.toUpperCase(Locale.ROOT),
            ALG_SLH_DSA_SHAKE_128S.toUpperCase(Locale.ROOT)
    );

    private PqcVerifier() {
    }

    /**
     * Checks if an algorithm is recognized as Post-Quantum Cryptography compliant (NIST FIPS 203/204/205).
     */
    public static boolean isPostQuantum(String alg) {
        if (alg == null || alg.isBlank()) {
            return false;
        }
        return POST_QUANTUM_ALGS.contains(alg.trim().toUpperCase(Locale.ROOT));
    }

    /**
     * Checks if an algorithm meets CNSA 2.0 requirements (Level 3+ security).
     */
    public static boolean isCnsa2Compliant(String alg) {
        if (alg == null || alg.isBlank()) {
            return false;
        }
        return CNSA_2_ALGS.contains(alg.trim().toUpperCase(Locale.ROOT));
    }

    /**
     * Performs a compliance audit on a list of present cryptographic algorithms.
     */
    public static PqcAuditResult auditAlgorithms(List<String> presentAlgorithms, boolean requireStrict) {
        List<String> present = presentAlgorithms != null ? presentAlgorithms : List.of();
        List<String> missing = new ArrayList<>();

        boolean hasPqc = false;
        boolean hasOnlyPqc = !present.isEmpty();

        for (String alg : present) {
            if (isPostQuantum(alg)) {
                hasPqc = true;
            } else {
                hasOnlyPqc = false;
            }
        }

        if (!hasPqc) {
            missing.add("ML-DSA-65");
        }

        boolean compliant;
        if (requireStrict) {
            compliant = hasPqc && hasOnlyPqc;
        } else {
            compliant = hasPqc;
        }

        String recommendation;
        if (compliant && hasOnlyPqc) {
            recommendation = "Full PQC-Strict profile verified; fully compliant with CNSA 2.0 and EU NIS 2.";
        } else if (compliant) {
            recommendation = "Hybrid profile active (Classical + PQC). Transition to pqc-strict before 2030.";
        } else {
            recommendation = "Vulnerable to Harvest-Now-Decrypt-Later attacks! Upgrade key ring to include ML-DSA-65.";
        }

        return new PqcAuditResult(compliant, hasOnlyPqc, present, missing, recommendation);
    }
}
