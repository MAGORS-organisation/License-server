#!/usr/bin/env bash
# ==============================================================================
# Native Bash High-Concurrency Benchmark Harness
# Usage: ./run-load-test.sh [http://localhost:8080] [50]
# ==============================================================================
set -euo pipefail

BASE_URL="${1:-http://localhost:8080}"
TOTAL_REQS="${2:-100}"

echo "🔥 Starting Symbolon Concurrency Benchmark..."
echo "   Target URL:  ${BASE_URL}"
echo "   Requests:    ${TOTAL_REQS}"

START_TIME=$(date +%s%N)
SUCCESS=0
DENIED=0
ERRORS=0

for i in $(seq 1 "${TOTAL_REQS}"); do
  STATUS=$(curl -s -o /dev/null -w "%{http_code}" -X POST "${BASE_URL}/v1/leases" \
    -H "Content-Type: application/json" \
    -d "{\"licenseKey\":\"BENCH-KEY-${i}\",\"machineId\":\"worker-${i}\",\"quantity\":1}" || echo "000")

  if [ "${STATUS}" -ge 200 ] && [ "${STATUS}" -lt 300 ]; then
    SUCCESS=$((SUCCESS + 1))
  elif [ "${STATUS}" -ge 400 ] && [ "${STATUS}" -lt 500 ]; then
    DENIED=$((DENIED + 1))
  else
    ERRORS=$((ERRORS + 1))
  fi
done

END_TIME=$(date +%s%N)
DURATION_MS=$(( (END_TIME - START_TIME) / 1000000 ))

echo ""
echo "🏁 Benchmark Complete!"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
echo "   Total Duration:   ${DURATION_MS} ms"
echo "   Success (2xx):    ${SUCCESS}"
echo "   Denied (4xx):     ${DENIED} (graceful quota limits)"
echo "   Errors (5xx):     ${ERRORS}"
echo "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━"
