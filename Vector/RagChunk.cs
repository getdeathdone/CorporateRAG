namespace CorporateRag.Vector;

public sealed record RagChunk(
    string Id,
    string Source,
    int Index,
    string Text);
