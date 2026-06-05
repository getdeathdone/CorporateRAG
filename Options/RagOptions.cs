namespace CorporateRag.Options;

public sealed class RagOptions
{
    public int ChunkSizeWords { get; set; } = 450;
    public int ChunkOverlapWords { get; set; } = 80;
    public int TopK { get; set; } = 5;
}
