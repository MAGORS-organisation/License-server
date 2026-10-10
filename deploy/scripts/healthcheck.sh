#!/usr/bin/env bash
# ==============================================================================
# Symbolon Operational Healthcheck Script
# ==============================================================================
set -euo pipefail

CP_URL="${1:-http://localhost:8080}"
RELAY_URL="${2:-http://localhost:8081}"

echo "🩺 Inspecting Symbolon Services Health..."

echo -n "1. Control Plane Liveness: "
curl -sSf "${CP_URL}/health/live" > /dev/null && echo "✅ OK" || (echo "❌ FAILED" && exit 1)

echo -n "2. Control Plane Readiness: "
curl -sSf "${CP_URL}/health/ready" > /dev/null && echo "✅ OK" || (echo "❌ FAILED" && exit 1)

echo -n "3. Control Plane Metrics: "
curl -sSf "${CP_URL}/metrics" > /dev/null && echo "✅ OK" || (echo "❌ FAILED" && exit 1)

echo -n "4. Relay Liveness: "
curl -sSf "${RELAY_URL}/health/live" > /dev/null && echo "✅ OK" || (echo "❌ FAILED" && exit 1)

echo -n "5. Relay Readiness: "
curl -sSf "${RELAY_URL}/health/ready" > /dev/null && echo "✅ OK" || (echo "❌ FAILED" && exit 1)

echo ""
echo "🎉 All services are fully operational!"
