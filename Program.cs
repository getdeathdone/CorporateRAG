using CorporateRag;
using CorporateRag.Cache;
using CorporateRag.Progress;
using CorporateRag.Rag;
using CorporateRag.Vector;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddCorporateRag(builder.Configuration);

var app = builder.Build();

var cacheStore = app.Services.GetRequiredService<IRagCacheStore>();
var vectorDatabase = app.Services.GetRequiredService<IInMemoryVectorDatabase>();
vectorDatabase.ReplaceAll(cacheStore.Load());

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/status", (
    IInMemoryVectorDatabase vectorDatabase,
    IIndexingProgress progress,
    IConfiguration configuration) =>
{
    var indexing = progress.Snapshot;

    return Results.Ok(new
    {
        provider = configuration["Ai:ActiveProvider"] ?? "Unknown",
        chunks = vectorDatabase.Count,
        cachedChunks = app.Services.GetRequiredService<IRagCacheStore>().Count,
        indexing
    });
});

app.MapGet("/api/dependencies", async (IConfiguration configuration, CancellationToken cancellationToken) =>
{
    var provider = configuration["Ai:ActiveProvider"] ?? "Unknown";
    var issues = new List<DependencyIssue>();
    var commands = new List<string>();

    if (provider.Equals("Local", StringComparison.OrdinalIgnoreCase))
    {
        var endpoint = configuration["Ai:Local:Endpoint"] ?? "http://localhost:11434";
        var chatModel = configuration["Ai:Local:ChatModel"] ?? "";
        var embeddingModel = configuration["Ai:Local:EmbeddingModel"] ?? "";

        try
        {
            using var httpClient = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(3)
            };

            var tagsJson = await httpClient.GetStringAsync($"{endpoint.TrimEnd('/')}/api/tags", cancellationToken);
            var installedModels = ReadOllamaModels(tagsJson);

            if (!ModelExists(installedModels, chatModel))
            {
                issues.Add(new DependencyIssue(
                    "Missing chat model",
                    $"Ollama model '{chatModel}' is not installed. Answers will not work."));
                commands.Add($"ollama pull {chatModel}");
            }

            if (!ModelExists(installedModels, embeddingModel))
            {
                issues.Add(new DependencyIssue(
                    "Missing embedding model",
                    $"Ollama model '{embeddingModel}' is not installed. PDF indexing will not work."));
                commands.Add($"ollama pull {embeddingModel}");
            }

            return Results.Ok(new
            {
                ok = issues.Count == 0,
                provider,
                endpoint,
                installedModels,
                issues,
                commands = commands.Distinct().ToArray()
            });
        }
        catch
        {
            issues.Add(new DependencyIssue(
                "Ollama is not reachable",
                $"Local provider is selected, but Ollama does not answer at {endpoint}."));
            commands.Add("Install Ollama from https://ollama.com/download/windows");
            commands.Add("ollama serve");
            commands.Add($"ollama pull {chatModel}");
            commands.Add($"ollama pull {embeddingModel}");

            return Results.Ok(new
            {
                ok = false,
                provider,
                endpoint,
                installedModels = Array.Empty<string>(),
                issues,
                commands = commands.Where(command => !string.IsNullOrWhiteSpace(command)).Distinct().ToArray()
            });
        }
    }

    if (provider.Equals("OpenAI", StringComparison.OrdinalIgnoreCase)
        && string.IsNullOrWhiteSpace(configuration["Ai:OpenAI:ApiKey"]))
    {
        issues.Add(new DependencyIssue(
            "Missing OpenAI API key",
            "OpenAI provider is selected, but Ai:OpenAI:ApiKey is empty."));
    }

    return Results.Ok(new
    {
        ok = issues.Count == 0,
        provider,
        issues,
        commands
    });
});

app.MapPost("/api/index", async (
    IFormFile file,
    IRagService rag,
    IInMemoryVectorDatabase vectorDatabase,
    IWebHostEnvironment environment,
    CancellationToken cancellationToken) =>
{
    try
    {
        if (file.Length == 0)
        {
            return Results.BadRequest(new { error = "Upload a non-empty PDF file." });
        }

        if (!file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
        {
            return Results.BadRequest(new { error = "Only PDF files are supported." });
        }

        var uploadsPath = Path.Combine(environment.ContentRootPath, "uploads");
        Directory.CreateDirectory(uploadsPath);

        var safeName = $"{Guid.NewGuid():N}-{Path.GetFileName(file.FileName)}";
        var pdfPath = Path.Combine(uploadsPath, safeName);

        await using (var stream = File.Create(pdfPath))
        {
            await file.CopyToAsync(stream, cancellationToken);
        }

        await rag.IndexPdfAsync(pdfPath, cancellationToken);
        app.Services.GetRequiredService<IRagCacheStore>().Save(vectorDatabase.GetAll());

        return Results.Ok(new
        {
            file = file.FileName,
            chunks = vectorDatabase.Count,
            cachedChunks = app.Services.GetRequiredService<IRagCacheStore>().Count
        });
    }
    catch (Exception exception)
    {
        var progress = app.Services.GetRequiredService<IIndexingProgress>();
        progress.Fail(ToClientMessage(exception));

        return Results.Problem(
            detail: ToClientMessage(exception),
            title: "PDF indexing failed",
            statusCode: StatusCodes.Status500InternalServerError);
    }
})
.DisableAntiforgery();

app.MapPost("/api/cache/clear", (
    IInMemoryVectorDatabase vectorDatabase,
    IRagCacheStore cacheStore,
    IIndexingProgress progress) =>
{
    vectorDatabase.Clear();
    cacheStore.Clear();
    progress.Complete("Cache cleared");

    return Results.Ok(new
    {
        chunks = vectorDatabase.Count,
        cachedChunks = cacheStore.Count
    });
});

app.MapPost("/api/ask", async (
    AskRequest request,
    IRagService rag,
    CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Question))
    {
        return Results.BadRequest(new { error = "Question is required." });
    }

    var answer = await rag.AskAsync(request.Question, cancellationToken);

    return Results.Ok(new
    {
        answer
    });
});

app.Run();

static string ToClientMessage(Exception exception)
{
    var message = exception.GetBaseException().Message;

    if (message.Contains("Connection refused", StringComparison.OrdinalIgnoreCase)
        || message.Contains("actively refused", StringComparison.OrdinalIgnoreCase)
        || message.Contains("No connection could be made", StringComparison.OrdinalIgnoreCase)
        || message.Contains("localhost:11434", StringComparison.OrdinalIgnoreCase))
    {
        return "Ollama is not reachable. Install/start Ollama, then run: ollama pull llama3.1:8b and ollama pull nomic-embed-text.";
    }

    return message;
}

static string[] ReadOllamaModels(string tagsJson)
{
    using var document = System.Text.Json.JsonDocument.Parse(tagsJson);
    if (!document.RootElement.TryGetProperty("models", out var models))
    {
        return [];
    }

    return models
        .EnumerateArray()
        .Select(model => model.TryGetProperty("name", out var name) ? name.GetString() : null)
        .Where(name => !string.IsNullOrWhiteSpace(name))
        .Select(name => name!)
        .ToArray();
}

static bool ModelExists(IEnumerable<string> installedModels, string requiredModel)
{
    if (string.IsNullOrWhiteSpace(requiredModel))
    {
        return false;
    }

    return installedModels.Any(model =>
        model.Equals(requiredModel, StringComparison.OrdinalIgnoreCase)
        || model.Equals($"{requiredModel}:latest", StringComparison.OrdinalIgnoreCase)
        || requiredModel.Equals($"{model}:latest", StringComparison.OrdinalIgnoreCase));
}

public sealed record AskRequest(string Question);
public sealed record DependencyIssue(string Title, string Detail);
