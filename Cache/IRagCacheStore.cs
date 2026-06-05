using CorporateRag.Vector;

namespace CorporateRag.Cache;

public interface IRagCacheStore
{
    int Count { get; }
    IReadOnlyList<(RagChunk Chunk, ReadOnlyMemory<float> Embedding)> Load();
    void Save(IEnumerable<(RagChunk Chunk, ReadOnlyMemory<float> Embedding)> items);
    void Clear();
}
