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

if ! command -v dotnet >/dev/null 2>&1; then
  echo "!! .NET SDK is missing."
  if command -v brew >/dev/null 2>&1; then
    brew install --cask dotnet-sdk
  else
    echo "Install .NET SDK from https://dotnet.microsoft.com/download/dotnet/8.0"
    open "https://dotnet.microsoft.com/download/dotnet/8.0"
    exit 1
  fi
fi

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

echo "==> Restoring and building project"
dotnet restore
dotnet build --no-restore

echo "==> Starting Corporate RAG"
pkill -f "CorporateRag" >/dev/null 2>&1 || true
pkill -f "dotnet run --no-build --urls $WEB_URL" >/dev/null 2>&1 || true
nohup dotnet run --no-build --urls "$WEB_URL" -- --no-open >/tmp/corporate-rag-web.log 2>&1 &

for _ in $(seq 1 40); do
  if curl -fsS "$WEB_URL/api/status" >/dev/null 2>&1; then
    echo "OK  Corporate RAG is running"
    open "$WEB_URL"
    exit 0
  fi
  sleep 0.5
done

echo "!! The web app did not start on $WEB_URL"
exit 1
