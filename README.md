# Corporate RAG

Corporate RAG is a local/web RAG application written in C# on .NET 8.

It lets a user upload PDF documents, index their text into vector embeddings, and ask questions over the indexed knowledge base. The application is built around Microsoft Semantic Kernel abstractions, so the business pipeline can work either with local Ollama models or with OpenAI API models without changing the RAG code.

## What It Does

1. Uploads a PDF from the web interface.
2. Extracts text from the PDF with PdfPig.
3. Splits the text into overlapping chunks.
4. Generates embeddings for each chunk.
5. Stores chunks and embeddings in memory for fast search.
6. Persists the indexed data into a local SQLite cache.
7. On question, embeds the question and finds the most relevant chunks by cosine similarity.
8. Sends the retrieved context to the chat model.
9. Shows the answer in the browser.

## What It Can Be Used For

- Internal document Q&A.
- Local experiments with RAG architecture.
- Comparing OpenAI and local Ollama models.
- Prototyping corporate knowledge assistants.
- Testing PDF ingestion, chunking, embeddings, and vector search.
- Demonstrating how to keep provider switching outside business logic.

This is a prototype-level local RAG system. For production, replace the in-memory vector DB with a real vector database such as Qdrant, Azure AI Search, PostgreSQL/pgvector, Weaviate, or similar.

## Quick Start

Recommended fast startup:

```cmd
start-rag-fast.bat
```

Higher-quality startup with a larger model:

```cmd
start-rag-quality.bat
```

Both files call `start-rag.ps1`, which checks and prepares the environment.

Fast mode uses:

```text
llama3.2:3b
```

Quality mode uses:

```text
llama3.1:8b
```

Both modes use this embedding model:

```text
nomic-embed-text:latest
```

## Hosted Web UI + Local Agent

The project also includes a hosted landing page pattern:

```text
hosted-ui/
```

This is intended for a flow like:

1. User opens a public/company website.
2. The page checks `http://localhost:5000/api/status`.
3. If the local agent is running, the page redirects to `http://localhost:5000`.
4. If the local agent is missing, the page shows Windows/macOS download buttons.
5. User downloads and runs the local package.
6. The local package starts the agent.
7. The hosted page detects the agent and opens the RAG app.

Build downloadable packages:

```powershell
.\build-installers.ps1
```

This creates:

```text
hosted-ui\downloads\corporate-rag-windows.zip
hosted-ui\downloads\corporate-rag-macos.zip
```

Build a Windows self-contained `.exe` package:

```powershell
.\publish-windows-exe.ps1
```

This creates:

```text
publish\windows-x64\CorporateRag.exe
publish\corporate-rag-windows-x64.zip
hosted-ui\downloads\corporate-rag-windows-x64.zip
```

The self-contained package does not require .NET to be installed on the target Windows machine. Ollama and the models are still required for local mode, so the included startup scripts still check/install Ollama and download the selected models.

Build a one-file Windows executable:

```powershell
.\publish-windows-single-exe.ps1
```

This creates:

```text
publish\windows-x64-single\CorporateRag.exe
hosted-ui\downloads\CorporateRag-win-x64.exe
```

This is a single self-contained application file. It does not include Ollama or local models, so local mode still requires Ollama and the selected models to be installed on the computer.

Build macOS self-contained packages:

```powershell
.\publish-macos-single.ps1
```

This creates:

```text
publish\osx-arm64\CorporateRag
publish\osx-x64\CorporateRag
publish\corporate-rag-osx-arm64.zip
publish\corporate-rag-osx-x64.zip
hosted-ui\downloads\corporate-rag-osx-arm64.zip
hosted-ui\downloads\corporate-rag-osx-x64.zip
```

Use `osx-arm64` for Apple Silicon Macs and `osx-x64` for Intel Macs. The macOS package still needs Ollama and models for local mode; `start-rag-mac.sh` checks and installs them through Homebrew when available.

Host the contents of `hosted-ui/` on any static hosting provider. For example:

```text
https://rag.yourcompany.com
```

The local backend has CORS enabled for this MVP, so the hosted page can check the local agent from the browser.

Important browser/security note: a website cannot silently install software on a user's computer. The user must download and run the installer/script. After that, the website can automatically detect the local agent and open the app.

After startup, the browser opens:

```text
http://localhost:5000
```

## One-Click Startup Behavior

The startup scripts try to make a new Windows machine ready automatically.

They check:

- .NET 8 SDK.
- Ollama.
- Required Ollama chat model.
- Required Ollama embedding model.
- NuGet dependencies.

If something is missing:

- If `winget` is available, the script tries to install .NET/Ollama through `winget`.
- If `winget` is not available, the script tries the official Ollama PowerShell installer.
- If that fails, the script falls back to direct installer download.
- If installation requires admin rights, the script restarts itself with administrator permissions.

Some Windows prompts cannot be avoided, especially UAC/admin confirmation and old `winget`/installer prompts.

## Manual Startup

If everything is already installed:

```powershell
dotnet restore
dotnet build
dotnet run --urls http://localhost:5000
```

Then open:

```text
http://localhost:5000
```

## Web Interface

The browser UI contains:

- PDF upload.
- `Index PDF` button.
- Indexing progress bar.
- Indexed chunk count.
- Cached chunk count.
- Answer generation indicator.
- Chat area.
- `Clear cache` button.
- Dependency warning modal if Ollama/models/API key are missing.

### Indexing Progress

During indexing the UI shows:

- Current chunk.
- Total chunks.
- Percent complete.
- Elapsed time.
- Estimated remaining time when enough progress is available.

### Answer Progress

During answer generation the UI shows:

- Animated thinking indicator.
- Elapsed answer time.
- Final answer duration.

## Local Cache

Indexed chunks and embeddings are persisted in SQLite:

```text
D:\_AI\RAG\data\rag-cache.db
```

On application startup, the app loads this cache into memory. This means already indexed documents can be used after restarting the app.

The `Clear cache` button clears:

- the in-memory vector database
- the SQLite cache

Uploaded PDF files are stored in:

```text
D:\_AI\RAG\uploads
```

Both `data/` and `uploads/` are ignored by git.

## Configuration

Configuration lives in:

```text
appsettings.json
```

Local Ollama mode:

```json
{
  "Ai": {
    "ActiveProvider": "Local",
    "Local": {
      "Endpoint": "http://localhost:11434",
      "ChatModel": "llama3.2:3b",
      "EmbeddingModel": "nomic-embed-text:latest"
    }
  }
}
```

OpenAI mode:

```json
{
  "Ai": {
    "ActiveProvider": "OpenAI",
    "OpenAI": {
      "ApiKey": "sk-...",
      "ChatModel": "gpt-4o-mini",
      "EmbeddingModel": "text-embedding-3-small"
    }
  }
}
```

Provider switching is done only through configuration. The RAG service itself depends on Semantic Kernel abstractions:

- `IChatCompletionService`
- `ITextEmbeddingGenerationService`

## Architecture

### Dependency Injection

Provider registration is handled in:

```text
DependencyInjection\ServiceCollectionExtensions.cs
```

It reads `appsettings.json` and registers either:

- OpenAI chat and embedding services
- Ollama chat and embedding services

### RAG Pipeline

Main RAG logic lives in:

```text
Rag\RagService.cs
```

It performs:

- PDF text extraction
- chunking
- embedding generation
- vector database upsert
- vector search
- prompt construction
- chat completion

### PDF Extraction

PDF text extraction is implemented with PdfPig:

```text
Pdf\PdfPigTextExtractor.cs
```

### Vector Search

In-memory vector storage is implemented in:

```text
Vector\InMemoryVectorDatabase.cs
```

It stores:

- chunk metadata
- chunk text
- embedding vector

Search uses cosine similarity.

### SQLite Cache

Persistent local cache is implemented in:

```text
Cache\SqliteRagCacheStore.cs
```

It stores chunks and embeddings as SQLite records.

### Progress Tracking

Indexing progress is implemented in:

```text
Progress\IndexingProgress.cs
```

The web UI reads progress through:

```text
GET /api/status
```

### Web API

Endpoints are defined in:

```text
Program.cs
```

Important endpoints:

```text
GET  /api/status
GET  /api/dependencies
POST /api/index
POST /api/ask
POST /api/cache/clear
```

### Frontend

Static frontend files:

```text
wwwroot\index.html
wwwroot\styles.css
wwwroot\app.js
```

The frontend uses plain HTML/CSS/JavaScript.

## Startup Files

Fast mode:

```text
start-rag-fast.bat
```

Quality mode:

```text
start-rag-quality.bat
```

Default fast alias:

```text
start-rag.bat
```

Main startup logic:

```text
start-rag.ps1
```

Ollama model pull helper:

```text
pull-ollama-model.cmd
```

The pull helper runs `ollama pull` through `cmd.exe`, because model downloads can behave better from Command Prompt than directly from PowerShell on some machines.

macOS launcher:

```text
start-rag-mac.sh
```

Run on macOS:

```bash
double-click install.command
```

If macOS blocks the file, open Terminal in the extracted folder and run:

```bash
xattr -dr com.apple.quarantine .
chmod +x install.command
./install.command
```

Advanced manual run:

```bash
chmod +x start-rag-mac.sh CorporateRag
./start-rag-mac.sh fast
./start-rag-mac.sh quality
```

## Troubleshooting

### Ollama Not Found

Run:

```powershell
ollama --version
```

If it is missing, run:

```powershell
irm https://ollama.com/install.ps1 | iex
```

From normal `cmd`, use:

```cmd
powershell -NoProfile -ExecutionPolicy Bypass -Command "irm https://ollama.com/install.ps1 | iex"
```

### Model Missing

Fast model:

```cmd
ollama pull llama3.2:3b
```

Quality model:

```cmd
ollama pull llama3.1:8b
```

Embedding model:

```cmd
ollama pull nomic-embed-text:latest
```

### Ollama Server Not Responding

Check:

```powershell
Invoke-WebRequest -UseBasicParsing http://localhost:11434/api/tags
```

Start manually:

```powershell
ollama serve
```

### Browser Shows Missing Dependency Window

The app checks dependencies through:

```text
GET /api/dependencies
```

If a model or Ollama is missing, the browser shows a setup window with the exact commands to run.

### Slow Model Download

Model downloads are performed by Ollama, not by the web app. `llama3.1:8b` is several GB. Use `start-rag-fast.bat` for a smaller model.

### PDF Indexing Is Empty

If the PDF is scanned/image-only, PdfPig may not extract text. This project does not currently include OCR.

### Port Already In Use

The app runs on:

```text
http://localhost:5000
```

If the port is busy, stop the existing app process or run manually with another URL:

```powershell
dotnet run --urls http://localhost:5010
```

## Git Ignored Runtime Files

The `.gitignore` excludes:

- build output
- uploaded PDFs
- SQLite cache
- downloaded setup installers
- IDE/user-local files

Important ignored paths:

```text
bin/
obj/
uploads/
data/
.setup/
```

## Notes

This project is intentionally small and local-first. It is useful as a working RAG foundation, integration demo, and architecture reference. For production use, add authentication, access control, persistent document metadata, a production vector database, structured logging, observability, and stronger PDF/OCR handling.
