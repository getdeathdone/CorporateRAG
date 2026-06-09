using CorporateRag.Options;
using Microsoft.Extensions.Options;
using System.Net.Http.Json;
using System.Text.Json;

namespace CorporateRag.Rag;

public sealed class OllamaChatAnswerGenerator : IChatAnswerGenerator
{
    private readonly HttpClient _httpClient;
    private readonly AiOptions _options;

    public OllamaChatAnswerGenerator(HttpClient httpClient, IOptions<AiOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public async Task<string> GenerateAnswerAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        var endpoint = _options.Local.Endpoint.TrimEnd('/');
        var request = new
        {
            model = _options.Local.ChatModel,
            stream = false,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            options = new
            {
                temperature = 0.2
            }
        };

        using var response = await _httpClient.PostAsJsonAsync(
            $"{endpoint}/api/chat",
            request,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        if (document.RootElement.TryGetProperty("message", out var message)
            && message.TryGetProperty("content", out var content))
        {
            var text = content.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }
        }

        if (document.RootElement.TryGetProperty("response", out var legacyResponse))
        {
            var text = legacyResponse.GetString();
            if (!string.IsNullOrWhiteSpace(text))
            {
                return text.Trim();
            }
        }

        throw new InvalidOperationException("Ollama returned an empty answer.");
    }
}
