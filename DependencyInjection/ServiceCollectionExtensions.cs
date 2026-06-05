using CorporateRag.Cache;
using CorporateRag.Options;
using CorporateRag.Pdf;
using CorporateRag.Progress;
using CorporateRag.Rag;
using CorporateRag.Vector;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;

namespace CorporateRag;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddCorporateRag(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var aiOptions = configuration.GetSection("Ai").Get<AiOptions>()
            ?? CreateDefaultAiOptions();

        services.Configure<AiOptions>(configuration.GetSection("Ai"));
        services.Configure<RagOptions>(configuration.GetSection("Rag"));

        RegisterAiServices(services, aiOptions);

        services.AddSingleton<IPdfTextExtractor, PdfPigTextExtractor>();
        services.AddSingleton<IIndexingProgress, IndexingProgress>();
        services.AddSingleton<IRagCacheStore, SqliteRagCacheStore>();
        services.AddSingleton<IInMemoryVectorDatabase, InMemoryVectorDatabase>();
        services.AddSingleton<IRagService, RagService>();

        services.AddSingleton<KernelPluginCollection>();
        services.AddTransient(serviceProvider => new Kernel(
            serviceProvider,
            serviceProvider.GetRequiredService<KernelPluginCollection>()));

        return services;
    }

    private static void RegisterAiServices(IServiceCollection services, AiOptions aiOptions)
    {
        if (aiOptions.ActiveProvider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase))
        {
            ValidateOpenAi(aiOptions.OpenAI);

            services.AddOpenAIChatCompletion(
                modelId: aiOptions.OpenAI.ChatModel,
                apiKey: aiOptions.OpenAI.ApiKey,
                orgId: aiOptions.OpenAI.OrganizationId);

            services.AddOpenAITextEmbeddingGeneration(
                modelId: aiOptions.OpenAI.EmbeddingModel,
                apiKey: aiOptions.OpenAI.ApiKey,
                orgId: aiOptions.OpenAI.OrganizationId);

            return;
        }

        if (aiOptions.ActiveProvider.Equals("Local", StringComparison.OrdinalIgnoreCase))
        {
            ValidateLocal(aiOptions.Local);

            var endpoint = new Uri(aiOptions.Local.Endpoint);

            services.AddOllamaChatCompletion(
                modelId: aiOptions.Local.ChatModel,
                endpoint: endpoint);

            services.AddOllamaTextEmbeddingGeneration(
                modelId: aiOptions.Local.EmbeddingModel,
                endpoint: endpoint);

            return;
        }

        throw new InvalidOperationException("Ai:ActiveProvider must be either 'Local' or 'OpenAI'.");
    }

    private static AiOptions CreateDefaultAiOptions()
    {
        return new AiOptions
        {
            ActiveProvider = "Local",
            Local = new LocalAiOptions
            {
                Endpoint = "http://localhost:11434",
                ChatModel = "llama3.2:3b",
                EmbeddingModel = "nomic-embed-text:latest"
            },
            OpenAI = new OpenAiOptions
            {
                ChatModel = "gpt-4o-mini",
                EmbeddingModel = "text-embedding-3-small"
            }
        };
    }

    private static void ValidateOpenAi(OpenAiOptions options)
    {
        Require(options.ApiKey, "Ai:OpenAI:ApiKey");
        Require(options.ChatModel, "Ai:OpenAI:ChatModel");
        Require(options.EmbeddingModel, "Ai:OpenAI:EmbeddingModel");
    }

    private static void ValidateLocal(LocalAiOptions options)
    {
        Require(options.Endpoint, "Ai:Local:Endpoint");
        Require(options.ChatModel, "Ai:Local:ChatModel");
        Require(options.EmbeddingModel, "Ai:Local:EmbeddingModel");

        if (!Uri.TryCreate(options.Endpoint, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("Ai:Local:Endpoint must be an absolute URI.");
        }
    }

    private static void Require(string value, string path)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException($"{path} is required.");
        }
    }
}
