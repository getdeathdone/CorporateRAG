#pragma warning disable SKEXP0001

using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Text;

namespace CorporateRag.Rag;

public sealed class SemanticKernelChatAnswerGenerator : IChatAnswerGenerator
{
    private readonly IChatCompletionService _chatService;

    public SemanticKernelChatAnswerGenerator(IChatCompletionService chatService)
    {
        _chatService = chatService;
    }

    public async Task<string> GenerateAnswerAsync(
        string systemPrompt,
        string userPrompt,
        CancellationToken cancellationToken = default)
    {
        var history = new ChatHistory();
        history.AddSystemMessage(systemPrompt);
        history.AddUserMessage(userPrompt);

        var streamedAnswer = new StringBuilder();
        await foreach (var chunk in _chatService.GetStreamingChatMessageContentsAsync(
                           history,
                           cancellationToken: cancellationToken))
        {
            streamedAnswer.Append(chunk.Content);
        }

        var streamedText = streamedAnswer.ToString();
        if (!string.IsNullOrWhiteSpace(streamedText))
        {
            return streamedText;
        }

        var response = await _chatService.GetChatMessageContentAsync(
            history,
            cancellationToken: cancellationToken);

        return ReadResponseText(response);
    }

    private static string ReadResponseText(ChatMessageContent response)
    {
        if (!string.IsNullOrWhiteSpace(response.Content))
        {
            return response.Content;
        }

        var itemText = string.Concat(response.Items.Select(item => item switch
        {
            TextContent textContent => textContent.Text,
            _ => item.ToString()
        }));

        return string.IsNullOrWhiteSpace(itemText)
            ? response.ToString()
            : itemText;
    }
}
