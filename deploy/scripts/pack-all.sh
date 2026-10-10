#!/usr/bin/env bash
# ==============================================================================
# Symbolon Multi-Platform SDK Packaging Script (Linux / macOS Bash)
# Builds and packages .NET NuGet, Python PyPI, and WASM npm packages.
# ==============================================================================

set -euo pipefail

OUTPUT_DIR="${1:-dist}"
CONFIGURATION="${2:-Release}"

echo "============================================================"
echo "  Symbolon Multi-Platform SDK Packaging Pipeline"
echo "============================================================"

rm -rf "${OUTPUT_DIR}"
mkdir -p "${OUTPUT_DIR}"
ABS_OUT="$(cd "${OUTPUT_DIR}" && pwd)"

# 1. Package .NET NuGet Packages
echo -e "\n[1/3] Packaging .NET Client & Protocol SDKs (NuGet)..."
DOTNET_PROJECTS=(
    "src/Symbolon.Crypto/Symbolon.Crypto.csproj"
    "src/Symbolon.Format/Symbolon.Format.csproj"
    "src/Symbolon.Protocol/Symbolon.Protocol.csproj"
    "src/Symbolon.Client/Symbolon.Client.csproj"
)

for proj in "${DOTNET_PROJECTS[@]}"; do
    echo "  -> Packing ${proj}"
    dotnet pack "${proj}" -c "${CONFIGURATION}" -o "${ABS_OUT}" --include-symbols -p:SymbolPackageFormat=snupkg
done

# 2. Package WASM npm Package
echo -e "\n[2/3] Packaging WASM / Browser Offline Validator (npm)..."
if [ -f "sdk/wasm/package.json" ]; then
    npm pack ./sdk/wasm --pack-destination "${ABS_OUT}"
fi

# 3. Package Python SDK
echo -e "\n[3/3] Packaging Python SDK (sdist & wheel)..."
if [ -f "sdk/python/symbolon/pyproject.toml" ]; then
    if python3 -c "import build" >/dev/null 2>&1; then
        python3 -m build sdk/python/symbolon --outdir "${ABS_OUT}"
    else
        echo "  [i] Python 'build' module not found; skipping python wheel."
    fi
fi

# 4. Generate Cryptographic Checksums (SHA-256)
echo -e "\n[4/4] Computing Cryptographic SHA-256 Checksums..."
cd "${ABS_OUT}"
if command -v sha256sum >/dev/null 2>&1; then
    sha256sum * > SHA256SUMS.txt
elif command -v shasum >/dev/null 2>&1; then
    shasum -a 256 * > SHA256SUMS.txt
fi

echo -e "\n✔ All SDK packages successfully built and hashed in: ${OUTPUT_DIR}"
