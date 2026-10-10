#!/usr/bin/env bash
# ==============================================================================
# Symbolon Production Stack Startup Script
# ==============================================================================
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
ROOT_DIR="$(cd "${SCRIPT_DIR}/../.." && pwd)"

echo "🚀 Starting Symbolon Licensing Server Stack..."

if [ ! -f "${ROOT_DIR}/.env" ]; then
    if [ -f "${ROOT_DIR}/.env.example" ]; then
        echo "⚠️  No .env file found. Creating .env from .env.example..."
        cp "${ROOT_DIR}/.env.example" "${ROOT_DIR}/.env"
    fi
fi

cd "${ROOT_DIR}"
docker compose up -d --build

echo "⏳ Waiting for services to become healthy..."
sleep 5

echo "🔍 Checking Control Plane health:"
curl -sSf http://localhost:8080/health/ready || echo "Control plane still starting..."

echo ""
echo "✅ Symbolon Stack is running!"
echo "   - Web Control Plane (Retro TUI): http://localhost:8080"
echo "   - Relay Proxy Node:              http://localhost:8081"
echo "   - Prometheus Metrics:            http://localhost:9090"
echo "   - Grafana Dashboards:            http://localhost:3000 (admin / admin)"
