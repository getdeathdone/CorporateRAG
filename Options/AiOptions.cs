namespace CorporateRag.Options;

public sealed class AiOptions
{
    public string ActiveProvider { get; set; } = "Local";
    public OpenAiOptions OpenAI { get; set; } = new();
    public LocalAiOptions Local { get; set; } = new();
}

public sealed class OpenAiOptions
{
    public string ApiKey { get; set; } = "";
    public string ChatModel { get; set; } = "";
    public string EmbeddingModel { get; set; } = "";
    public string? OrganizationId { get; set; }
}

public sealed class LocalAiOptions
{
    public string Endpoint { get; set; } = "http://localhost:11434";
    public string ChatModel { get; set; } = "";
    public string EmbeddingModel { get; set; } = "";
}
