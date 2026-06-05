#!/usr/bin/env bash
set -euo pipefail

ARCHIVE_URL="${1:-}"
RUNTIME="${2:-}"

if [ -z "$ARCHIVE_URL" ] || [ -z "$RUNTIME" ]; then
  echo "Usage: install-mac.sh <archive-url> <osx-arm64|osx-x64>"
  exit 1
fi

case "$RUNTIME" in
  osx-arm64|osx-x64)
    ;;
  *)
    echo "Unsupported runtime: $RUNTIME"
    exit 1
    ;;
esac

ARCHIVE_NAME="corporate-rag-$RUNTIME.zip"
TARGET_ROOT="$HOME/Downloads/CorporateRag-$RUNTIME"
ARCHIVE_PATH="$HOME/Downloads/$ARCHIVE_NAME"

echo "Corporate RAG macOS setup"
echo "Runtime: $RUNTIME"
echo

mkdir -p "$TARGET_ROOT"

echo "==> Downloading package"
curl -fL --retry 3 "$ARCHIVE_URL" -o "$ARCHIVE_PATH"

echo "==> Extracting package"
rm -rf "$TARGET_ROOT"
mkdir -p "$TARGET_ROOT"
if command -v python3 >/dev/null 2>&1; then
  python3 - "$ARCHIVE_PATH" "$TARGET_ROOT" <<'PY'
import os
import sys
import zipfile

archive_path = sys.argv[1]
target_root = sys.argv[2]

with zipfile.ZipFile(archive_path) as archive:
    for member in archive.infolist():
        name = member.filename.replace("\\", "/")
        if not name or name.endswith("/"):
            continue
        output_path = os.path.abspath(os.path.join(target_root, name))
        target_abs = os.path.abspath(target_root)
        if not output_path.startswith(target_abs + os.sep):
            raise RuntimeError(f"Unsafe zip entry: {member.filename}")
        os.makedirs(os.path.dirname(output_path), exist_ok=True)
        with archive.open(member) as source, open(output_path, "wb") as output:
            output.write(source.read())
PY
else
  unzip -q "$ARCHIVE_PATH" -d "$TARGET_ROOT" || true
fi

echo "==> Removing macOS quarantine attributes"
if command -v xattr >/dev/null 2>&1; then
  xattr -dr com.apple.quarantine "$TARGET_ROOT" >/dev/null 2>&1 || true
fi

echo "==> Preparing launcher"
chmod +x "$TARGET_ROOT/install.command" 2>/dev/null || true
chmod +x "$TARGET_ROOT/start-rag-mac.sh" 2>/dev/null || true
chmod +x "$TARGET_ROOT/CorporateRag" 2>/dev/null || true

echo "==> Starting Corporate RAG"
cd "$TARGET_ROOT"
./install.command
