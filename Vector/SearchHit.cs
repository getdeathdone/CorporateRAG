namespace CorporateRag.Vector;

public sealed record SearchHit(
    RagChunk Chunk,
    float Score);
