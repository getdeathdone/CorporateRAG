# Corporate RAG

Minimal console RAG application on C# with Microsoft Semantic Kernel.

The business pipeline depends on Semantic Kernel abstractions only:

- `IChatCompletionService`
- `ITextEmbeddingGenerationService`

Switching between OpenAI and local Ollama is done in `appsettings.json`.

## Prerequisites

- .NET 8 SDK
- For local mode: Ollama running on `http://localhost:11434`
- Local chat model, for example:

```powershell
ollama pull llama3.1:8b
```

- Local embedding model:

```powershell
ollama pull nomic-embed-text
```

## Run

Recommended one-click Windows start:

```powershell
.\start-rag.bat
```

It checks .NET, Ollama, required models, restores packages, builds the app, starts the web server, and opens the browser.

If `winget` is available, it can also install missing .NET SDK and Ollama automatically. On first run, downloading `llama3.1:8b` can take a while because the model is several GB.

Manual start:

```powershell
dotnet restore
dotnet run
```

Then open:

```text
http://localhost:5000
```

Upload a PDF and ask questions in the web interface.

## Local cache

Indexed chunks and embeddings are persisted in:

```text
D:\_AI\RAG\data\rag-cache.db
```

The app loads this cache on startup. Use the `Clear cache` button in the web UI to clear both the in-memory vector database and the local SQLite cache.

## Switch provider

Use local Ollama:

```json
"ActiveProvider": "Local"
```

Use OpenAI:

```json
"ActiveProvider": "OpenAI"
```

Then set `Ai:OpenAI:ApiKey`, `Ai:OpenAI:ChatModel`, and `Ai:OpenAI:EmbeddingModel`.
