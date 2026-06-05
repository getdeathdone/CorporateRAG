namespace CorporateRag.Vector;

public interface IInMemoryVectorDatabase
{
    void Upsert(RagChunk chunk, ReadOnlyMemory<float> embedding);
    IReadOnlyList<(RagChunk Chunk, ReadOnlyMemory<float> Embedding)> GetAll();
    void ReplaceAll(IEnumerable<(RagChunk Chunk, ReadOnlyMemory<float> Embedding)> items);
    void Clear();
    IReadOnlyList<SearchHit> Search(ReadOnlyMemory<float> queryEmbedding, int topK);
    int Count { get; }
}
