#pragma warning disable SKEXP0001

using CorporateRag.Options;
using CorporateRag.Pdf;
using CorporateRag.Progress;
using CorporateRag.Vector;
using Microsoft.Extensions.Options;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Embeddings;

namespace CorporateRag.Rag;

public sealed class RagService : IRagService
{
    private readonly IPdfTextExtractor _pdfTextExtractor;
    private readonly ITextEmbeddingGenerationService _embeddingService;
    private readonly IChatCompletionService _chatService;
    private readonly IInMemoryVectorDatabase _vectorDatabase;
    private readonly IIndexingProgress _progress;
    private readonly RagOptions _options;

    public RagService(
        IPdfTextExtractor pdfTextExtractor,
        ITextEmbeddingGenerationService embeddingService,
        IChatCompletionService chatService,
        IInMemoryVectorDatabase vectorDatabase,
        IIndexingProgress progress,
        IOptions<RagOptions> options)
    {
        _pdfTextExtractor = pdfTextExtractor;
        _embeddingService = embeddingService;
        _chatService = chatService;
        _vectorDatabase = vectorDatabase;
        _progress = progress;
        _options = options.Value;
    }

    public async Task IndexPdfAsync(string pdfPath, CancellationToken cancellationToken = default)
    {
        _progress.Preparing(Path.GetFileName(pdfPath), "Reading PDF");

        var text = _pdfTextExtractor.ExtractText(pdfPath);
        _progress.Preparing(Path.GetFileName(pdfPath), "Preparing chunks");

        var chunks = ChunkText(
            text,
            pdfPath,
            _options.ChunkSizeWords,
            _options.ChunkOverlapWords);

        _progress.Start(Path.GetFileName(pdfPath), chunks.Count);

        foreach (var chunk in chunks)
        {
            _progress.Advance(chunk.Index, $"Embedding chunk {chunk.Index + 1} of {chunks.Count}");

            var embedding = await _embeddingService.GenerateEmbeddingAsync(
                chunk.Text,
                cancellationToken: cancellationToken);

            _vectorDatabase.Upsert(chunk, embedding);
            _progress.Advance(chunk.Index + 1, $"Indexed chunk {chunk.Index + 1} of {chunks.Count}");
        }

        _progress.Complete($"Indexed {chunks.Count} chunks");
    }

    public async Task<string> AskAsync(string question, CancellationToken cancellationToken = default)
    {
        if (_vectorDatabase.Count == 0)
        {
            return "Knowledge base is empty. Index a PDF first.";
        }

        var questionEmbedding = await _embeddingService.GenerateEmbeddingAsync(
            question,
            cancellationToken: cancellationToken);

        var hits = _vectorDatabase.Search(questionEmbedding, _options.TopK);

        var context = string.Join(
            Environment.NewLine + Environment.NewLine + "---" + Environment.NewLine + Environment.NewLine,
            hits.Select(hit =>
                $"Source: {hit.Chunk.Source}; chunk: {hit.Chunk.Index}; score: {hit.Score:F3}{Environment.NewLine}{hit.Chunk.Text}"));

        var history = new ChatHistory();
        history.AddSystemMessage("""
            You are a corporate RAG assistant.
            Answer only from the provided context.
            If the context is insufficient, say that the knowledge base does not contain enough information.
            Keep the answer concise and cite chunk numbers when useful.
            """);

        history.AddUserMessage($"""
            Context:
            {context}

            Question:
            {question}
            """);

        var response = await _chatService.GetChatMessageContentAsync(
            history,
            cancellationToken: cancellationToken);

        return response.Content ?? "";
    }

    private static IReadOnlyList<RagChunk> ChunkText(
        string text,
        string source,
        int chunkSizeWords,
        int chunkOverlapWords)
    {
        if (chunkSizeWords <= 0)
        {
            throw new InvalidOperationException("Rag:ChunkSizeWords must be greater than zero.");
        }

        if (chunkOverlapWords < 0 || chunkOverlapWords >= chunkSizeWords)
        {
            throw new InvalidOperationException("Rag:ChunkOverlapWords must be non-negative and less than Rag:ChunkSizeWords.");
        }

        var words = text.Split(
            [' ', '\r', '\n', '\t'],
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        var chunks = new List<RagChunk>();
        var step = chunkSizeWords - chunkOverlapWords;

        for (var start = 0; start < words.Length; start += step)
        {
            var chunkText = string.Join(' ', words.Skip(start).Take(chunkSizeWords));
            if (string.IsNullOrWhiteSpace(chunkText))
            {
                continue;
            }

            chunks.Add(new RagChunk(
                Id: $"{Path.GetFileName(source)}-{chunks.Count}",
                Source: source,
                Index: chunks.Count,
                Text: chunkText));
        }

        return chunks;
    }
}
