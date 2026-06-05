#!/bin/bash
set -e

SCRIPT_DIR="$(cd "$(dirname "$0")" && pwd)"
cd "$SCRIPT_DIR"

echo "Corporate RAG macOS installer"
echo "Working directory: $SCRIPT_DIR"
echo

if command -v xattr >/dev/null 2>&1; then
  echo "Removing macOS quarantine attributes..."
  xattr -dr com.apple.quarantine . >/dev/null 2>&1 || true
fi

chmod +x ./CorporateRag 2>/dev/null || true
chmod +x ./start-rag-mac.sh

echo "Starting Corporate RAG in fast mode..."
echo
./start-rag-mac.sh fast

echo
echo "Done. You can close this window."
