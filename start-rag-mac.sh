#!/usr/bin/env bash
set -euo pipefail

MODE="${1:-fast}"
PROJECT_ROOT="$(cd "$(dirname "$0")" && pwd)"
WEB_URL="http://localhost:5000"
OLLAMA_ENDPOINT="http://localhost:11434"
CHAT_MODEL="llama3.2:3b"
EMBEDDING_MODEL="nomic-embed-text:latest"

if [ "$MODE" = "quality" ]; then
  CHAT_MODEL="llama3.1:8b"
fi

cd "$PROJECT_ROOT"

echo "==> Selected startup mode"
echo "OK  Mode: $MODE"
echo "OK  Chat model: $CHAT_MODEL"

if ! command -v ollama >/dev/null 2>&1; then
  echo "!! Ollama is missing."
  if command -v brew >/dev/null 2>&1; then
    brew install --cask ollama
  else
    echo "Install Ollama from https://ollama.com/download"
    open "https://ollama.com/download"
    exit 1
  fi
fi

if ! curl -fsS "$OLLAMA_ENDPOINT/api/tags" >/dev/null 2>&1; then
  echo "==> Starting Ollama"
  nohup ollama serve >/tmp/corporate-rag-ollama.log 2>&1 &
  sleep 5
fi

echo "==> Checking local models"
ollama pull "$CHAT_MODEL"
ollama pull "$EMBEDDING_MODEL"

echo "==> Starting Corporate RAG"
pkill -f "CorporateRag" >/dev/null 2>&1 || true
if command -v xattr >/dev/null 2>&1; then
  xattr -dr com.apple.quarantine . >/dev/null 2>&1 || true
fi
chmod +x ./CorporateRag 2>/dev/null || true
if command -v codesign >/dev/null 2>&1; then
  codesign --force --deep --sign - ./CorporateRag >/tmp/corporate-rag-codesign.log 2>&1 || true
fi

rm -f /tmp/corporate-rag-web.log
set +e
nohup ./CorporateRag --urls "$WEB_URL" --no-open >/tmp/corporate-rag-web.log 2>&1 &
APP_PID=$!
set -e

for _ in $(seq 1 40); do
  if ! kill -0 "$APP_PID" >/dev/null 2>&1; then
    echo "!! Corporate RAG process stopped."
    if [ -f /tmp/corporate-rag-web.log ]; then
      echo "==> Web app log"
      tail -n 80 /tmp/corporate-rag-web.log
    fi
    if [ -f /tmp/corporate-rag-codesign.log ]; then
      echo "==> Codesign log"
      tail -n 40 /tmp/corporate-rag-codesign.log
    fi
    exit 1
  fi

  if curl -fsS "$WEB_URL/api/status" >/dev/null 2>&1; then
    echo "OK  Corporate RAG is running"
    open "$WEB_URL"
    exit 0
  fi
  sleep 0.5
done

echo "!! The web app did not start on $WEB_URL"
exit 1
