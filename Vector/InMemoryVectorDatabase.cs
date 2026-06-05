namespace CorporateRag.Vector;

public sealed class InMemoryVectorDatabase : IInMemoryVectorDatabase
{
    private readonly List<(RagChunk Chunk, ReadOnlyMemory<float> Embedding)> _items = [];
    private readonly object _gate = new();

    public int Count
    {
        get
        {
            lock (_gate)
            {
                return _items.Count;
            }
        }
    }

    public void Upsert(RagChunk chunk, ReadOnlyMemory<float> embedding)
    {
        lock (_gate)
        {
            _items.Add((chunk, embedding));
        }
    }

    public IReadOnlyList<(RagChunk Chunk, ReadOnlyMemory<float> Embedding)> GetAll()
    {
        lock (_gate)
        {
            return _items
                .Select(item => (item.Chunk, Embedding: (ReadOnlyMemory<float>)item.Embedding.ToArray()))
                .ToList();
        }
    }

    public void ReplaceAll(IEnumerable<(RagChunk Chunk, ReadOnlyMemory<float> Embedding)> items)
    {
        lock (_gate)
        {
            _items.Clear();
            _items.AddRange(items.Select(item => (item.Chunk, Embedding: (ReadOnlyMemory<float>)item.Embedding.ToArray())));
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _items.Clear();
        }
    }

    public IReadOnlyList<SearchHit> Search(ReadOnlyMemory<float> queryEmbedding, int topK)
    {
        List<(RagChunk Chunk, ReadOnlyMemory<float> Embedding)> snapshot;
        lock (_gate)
        {
            snapshot = _items
                .Select(item => (item.Chunk, Embedding: (ReadOnlyMemory<float>)item.Embedding.ToArray()))
                .ToList();
        }

        return snapshot
            .Select(item => new SearchHit(
                item.Chunk,
                CosineSimilarity(queryEmbedding.Span, item.Embedding.Span)))
            .OrderByDescending(hit => hit.Score)
            .Take(topK)
            .ToList();
    }

    private static float CosineSimilarity(ReadOnlySpan<float> a, ReadOnlySpan<float> b)
    {
        var length = Math.Min(a.Length, b.Length);
        if (length == 0)
        {
            return 0;
        }

        double dot = 0;
        double normA = 0;
        double normB = 0;

        for (var i = 0; i < length; i++)
        {
            dot += a[i] * b[i];
            normA += a[i] * a[i];
            normB += b[i] * b[i];
        }

        if (normA == 0 || normB == 0)
        {
            return 0;
        }

        return (float)(dot / (Math.Sqrt(normA) * Math.Sqrt(normB)));
    }
}
