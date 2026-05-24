#!/usr/bin/env bash
# =============================================================================
# Kusar.sh — Startup Script Linux/macOS
# Robocode Tank Royale | Kusar v1.0
# =============================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
cd "$SCRIPT_DIR"

echo "============================================================"
echo " Kusar v1.0 — Greedy Adaptive Orbit Bot"
echo "============================================================"
echo ""

if ! command -v dotnet &>/dev/null; then
    echo "[ERROR] .NET Runtime tidak ditemukan."
    exit 1
fi

DOTNET_VERSION=$(dotnet --version)

echo "[INFO] .NET versi: $DOTNET_VERSION"
echo ""

export SERVER_URL=${SERVER_URL:-ws://localhost:7654}
export SERVER_SECRET=${SERVER_SECRET:-}

if [[ ! -f "bin/Release/net6.0/Kusar.dll" ]]; then
    echo "[INFO] Building Kusar..."
    dotnet build -c Release --nologo
fi

echo "[INFO] Menjalankan Kusar..."
echo "[INFO] Server: $SERVER_URL"
echo ""

dotnet run --project Kusar.csproj -c Release --no-build
