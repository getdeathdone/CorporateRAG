namespace CorporateRag.Rag;

public interface IChatAnswerGenerator
{
    Task<string> GenerateAnswerAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default);
}
