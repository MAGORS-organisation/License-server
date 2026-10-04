package io.symbolon.models;

import java.util.List;

/**
 * PQC compliance verification result.
 */
public record PqcAuditResult(
        boolean isCompliant,
        boolean isStrictPqc,
        List<String> presentAlgorithms,
        List<String> missingAlgorithms,
        String recommendation
) {
}
