#!/usr/bin/env bash
# ==============================================================================
# Symbolon PQC Test Execution Gating Script (§10.10)
#
# Asserts that Post-Quantum Cryptography (FIPS 203 ML-KEM, FIPS 204 ML-DSA,
# FIPS 205 SLH-DSA) test cases were actively executed and NOT silently skipped.
# ==============================================================================

set -euo pipefail

LOG_DIR="${1:-./TestResults}"
TRX_FILE=$(find "${LOG_DIR}" -name "*.trx" -type f 2>/dev/null | head -n 1 || true)

echo "=== Symbolon PQC Test Coverage Assertion ==="

if [[ -z "${TRX_FILE}" || ! -f "${TRX_FILE}" ]]; then
    echo "Running Symbolon.Crypto.Tests to verify PQC coverage..."
    dotnet test tests/Symbolon.Crypto.Tests/Symbolon.Crypto.Tests.csproj \
        --configuration Release \
        --logger "trx;LogFileName=pqc-verification.trx" \
        --results-directory "${LOG_DIR}" \
        --verbosity normal
    TRX_FILE="${LOG_DIR}/pqc-verification.trx"
fi

if [[ ! -f "${TRX_FILE}" ]]; then
    echo "ERROR: Test results file ${TRX_FILE} not found!" >&2
    exit 1
fi

echo "Analyzing test results in: ${TRX_FILE}"

# Check for ML-DSA, ML-KEM, and SLH-DSA test passes
ML_DSA_PASSED=$(grep -c 'testName=.*MlDsa.*outcome="Passed"' "${TRX_FILE}" || true)
ML_KEM_PASSED=$(grep -c 'testName=.*MlKem.*outcome="Passed"' "${TRX_FILE}" || true)
SLH_DSA_PASSED=$(grep -c 'testName=.*SlhDsa.*outcome="Passed"' "${TRX_FILE}" || true)

TOTAL_PQC_PASSED=$((ML_DSA_PASSED + ML_KEM_PASSED + SLH_DSA_PASSED))
SKIPPED_TESTS=$(grep -c 'outcome="NotExecuted"' "${TRX_FILE}" || true)

echo "PQC Test Summary:"
echo " - ML-DSA (FIPS 204) Passed:  ${ML_DSA_PASSED}"
echo " - ML-KEM (FIPS 203) Passed:  ${ML_KEM_PASSED}"
echo " - SLH-DSA (FIPS 205) Passed: ${SLH_DSA_PASSED}"
echo " - Total PQC Tests Passed:    ${TOTAL_PQC_PASSED}"
echo " - Skipped Tests:             ${SKIPPED_TESTS}"

if [[ ${TOTAL_PQC_PASSED} -eq 0 ]]; then
    echo "CRITICAL SECURITY ERROR: Zero Post-Quantum Cryptography tests were executed!" >&2
    echo "Environment may be missing liboqs, modern OpenSSL (>= 3.5), or .NET 10 PQC primitives." >&2
    exit 1
fi

if [[ ${SKIPPED_TESTS} -gt 0 ]]; then
    echo "WARNING / REJECTION: ${SKIPPED_TESTS} tests were skipped! PQC tests must NEVER be silently skipped." >&2
    exit 1
fi

echo "✔ SUCCESS: PQC test coverage assertion passed (${TOTAL_PQC_PASSED} tests executed, 0 skipped)."
exit 0
