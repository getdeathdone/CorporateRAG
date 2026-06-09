using CorporateRag;
using CorporateRag.Cache;
using CorporateRag.Progress;
using CorporateRag.Rag;
using CorporateRag.Vector;
using Microsoft.Extensions.FileProviders;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

var appDataRoot = Path.Combine(
    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
    "CorporateRag");
Directory.CreateDirectory(appDataRoot);
var userSettingsPath = Path.Combine(appDataRoot, "user-settings.json");

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = appDataRoot
});
var configuredUrls = ReadArgumentValue(args, "--urls");
if (!string.IsNullOrWhiteSpace(configuredUrls))
{
    builder.WebHost.UseUrls(configuredUrls);
}

builder.Configuration.AddJsonFile(userSettingsPath, optional: true, reloadOnChange: false);
var shouldOpenBrowser = !args.Any(arg => arg.Equals("--no-open", StringComparison.OrdinalIgnoreCase));
var modelSetup = new ModelSetupState();
builder.Services.AddCors(options =>
{
    options.AddPolicy("HostedUi", policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});
builder.Services.AddCorporateRag(builder.Configuration);

var webRootPath = EmbeddedWebRoot.EnsureExtracted(appDataRoot);
builder.Environment.WebRootPath = webRootPath;
builder.Environment.WebRootFileProvider = new PhysicalFileProvider(builder.Environment.WebRootPath);

var app = builder.Build();

var cacheStore = app.Services.GetRequiredService<IRagCacheStore>();
var vectorDatabase = app.Services.GetRequiredService<IInMemoryVectorDatabase>();
vectorDatabase.ReplaceAll(cacheStore.Load());

_ = Task.Run(() => EnsureLocalModelsAsync(app.Configuration, modelSetup, app.Logger, userSettingsPath));

var webRootFileProvider = new PhysicalFileProvider(builder.Environment.WebRootPath);

app.UseDefaultFiles(new DefaultFilesOptions
{
    FileProvider = webRootFileProvider
});
app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = webRootFileProvider
});
app.UseCors("HostedUi");

app.MapGet("/", (IWebHostEnvironment environment) =>
{
    return Results.Content(
        File.ReadAllText(Path.Combine(environment.WebRootPath, "index.html")),
        "text/html");
});

app.MapGet("/index.html", (IWebHostEnvironment environment) =>
{
    return Results.Content(
        File.ReadAllText(Path.Combine(environment.WebRootPath, "index.html")),
        "text/html");
});

app.MapGet("/app.js", (IWebHostEnvironment environment) =>
{
    return Results.Content(
        File.ReadAllText(Path.Combine(environment.WebRootPath, "app.js")),
        "text/javascript");
});

app.MapGet("/styles.css", (IWebHostEnvironment environment) =>
{
    return Results.Content(
        File.ReadAllText(Path.Combine(environment.WebRootPath, "styles.css")),
        "text/css");
});

app.MapGet("/api/status", (
    IInMemoryVectorDatabase vectorDatabase,
    IIndexingProgress progress,
    IConfiguration configuration) =>
{
    var indexing = progress.Snapshot;

    return Results.Ok(new
    {
        provider = configuration["Ai:ActiveProvider"] ?? "Local",
        chunks = vectorDatabase.Count,
        cachedChunks = app.Services.GetRequiredService<IRagCacheStore>().Count,
        indexing,
        modelSetup
    });
});

app.MapGet("/api/dependencies", async (IConfiguration configuration, CancellationToken cancellationToken) =>
{
    var provider = configuration["Ai:ActiveProvider"] ?? "Local";
    var issues = new List<DependencyIssue>();
    var commands = new List<string>();

    if (provider.Equals("Local", StringComparison.OrdinalIgnoreCase))
    {
        var endpoint = configuration["Ai:Local:Endpoint"] ?? "http://localhost:11434";
        var chatModel = configuration["Ai:Local:ChatModel"] ?? "llama3.2:3b";
        var embeddingModel = configuration["Ai:Local:EmbeddingModel"] ?? "nomic-embed-text:latest";

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
                commands = commands.Distinct().ToArray(),
                modelSetup
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
                commands = commands.Where(command => !string.IsNullOrWhiteSpace(command)).Distinct().ToArray(),
                modelSetup
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
        commands,
        modelSetup
    });
});

app.MapGet("/api/models", async (IConfiguration configuration) =>
{
    var endpoint = configuration["Ai:Local:Endpoint"] ?? "http://localhost:11434";
    var installedModels = Array.Empty<string>();
    var ollamaReachable = false;
    var error = "";

    try
    {
        installedModels = await GetInstalledOllamaModelsAsync(endpoint);
        ollamaReachable = true;
    }
    catch (Exception exception)
    {
        error = ToClientMessage(exception);
    }

    return Results.Ok(new
    {
        ollamaReachable,
        error,
        endpoint,
        selected = new
        {
            chatModel = configuration["Ai:Local:ChatModel"] ?? "llama3.2:3b",
            embeddingModel = configuration["Ai:Local:EmbeddingModel"] ?? "nomic-embed-text:latest",
            saved = File.Exists(userSettingsPath)
        },
        installedModels,
        presets = GetModelPresets(),
        modelSetup
    });
});

app.MapPost("/api/models/select", (ModelSelectionRequest request, IConfiguration configuration) =>
{
    if (string.IsNullOrWhiteSpace(request.ChatModel))
    {
        return Results.BadRequest(new { error = "Chat model is required." });
    }

    var embeddingModel = string.IsNullOrWhiteSpace(request.EmbeddingModel)
        ? "nomic-embed-text:latest"
        : request.EmbeddingModel;

    _ = Task.Run(async () =>
    {
        try
        {
            var endpoint = configuration["Ai:Local:Endpoint"] ?? "http://localhost:11434";
            var requiredModels = new[] { request.ChatModel, embeddingModel }
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            modelSetup.MarkRunning("Preparing selected models", requiredModels.Length);
            await PullMissingModelsAsync(endpoint, requiredModels, modelSetup);
            WriteUserModelSettings(userSettingsPath, request.ChatModel, embeddingModel);
            modelSetup.MarkCompleted("Model selection saved. Restarting app...");
            RestartApplication(shouldOpenBrowser);
            await app.StopAsync();
        }
        catch (Exception exception)
        {
            app.Logger.LogWarning(exception, "Model selection failed.");
            modelSetup.MarkFailed(ToClientMessage(exception));
        }
    });

    return Results.Accepted(value: new
    {
        message = "Model setup started",
        chatModel = request.ChatModel,
        embeddingModel
    });
});

app.MapPost("/api/ollama/install", () =>
{
    _ = Task.Run(async () =>
    {
        try
        {
            modelSetup.MarkRunning("Installing Ollama", 1);
            await InstallOllamaAsync(appDataRoot, modelSetup);
            modelSetup.MarkCompleted("Ollama installed. Choose a model.");
        }
        catch (Exception exception)
        {
            app.Logger.LogWarning(exception, "Ollama installation failed.");
            modelSetup.MarkFailed(ToClientMessage(exception));
        }
    });

    return Results.Accepted(value: new
    {
        message = "Ollama installation started"
    });
});

app.MapPost("/api/ollama/start", async (IConfiguration configuration) =>
{
    try
    {
        var endpoint = configuration["Ai:Local:Endpoint"] ?? "http://localhost:11434";
        StartOllamaIfNeeded(endpoint);
        await WaitForOllamaAsync(endpoint, TimeSpan.FromSeconds(20));

        return Results.Ok(new
        {
            message = "Ollama is running"
        });
    }
    catch (Exception exception)
    {
        return Results.Problem(
            detail: ToClientMessage(exception),
            title: "Ollama start failed",
            statusCode: StatusCodes.Status500InternalServerError);
    }
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

        var uploadsPath = Path.Combine(appDataRoot, "uploads");
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

if (shouldOpenBrowser)
{
    _ = Task.Run(async () =>
    {
        await Task.Delay(1200);
        OpenBrowser("http://localhost:5000");
    });
}

app.Run();

static string ToClientMessage(Exception exception)
{
    var message = exception.GetBaseException().Message;

    if (message.Contains("Connection refused", StringComparison.OrdinalIgnoreCase)
        || message.Contains("actively refused", StringComparison.OrdinalIgnoreCase)
        || message.Contains("No connection could be made", StringComparison.OrdinalIgnoreCase)
        || message.Contains("localhost:11434", StringComparison.OrdinalIgnoreCase))
    {
        return "Ollama is not reachable. Install/start Ollama, then run: ollama pull llama3.2:3b and ollama pull nomic-embed-text:latest.";
    }

    return message;
}

static string? ReadArgumentValue(string[] args, string name)
{
    for (var index = 0; index < args.Length; index++)
    {
        var arg = args[index];
        if (arg.Equals(name, StringComparison.OrdinalIgnoreCase)
            && index + 1 < args.Length)
        {
            return args[index + 1];
        }

        var prefix = $"{name}=";
        if (arg.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return arg[prefix.Length..];
        }
    }

    return null;
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

static void OpenBrowser(string url)
{
    try
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = url,
            UseShellExecute = true
        });
    }
    catch
    {
        // Browser auto-open is a convenience only. The server still runs if this fails.
    }
}

static async Task EnsureLocalModelsAsync(IConfiguration configuration, ModelSetupState state, ILogger logger, string userSettingsPath)
{
    var provider = configuration["Ai:ActiveProvider"] ?? "Local";
    if (!provider.Equals("Local", StringComparison.OrdinalIgnoreCase))
    {
        state.MarkSkipped("OpenAI provider is selected");
        return;
    }

    if (!File.Exists(userSettingsPath))
    {
        state.MarkSkipped("Choose a local model to use");
        return;
    }

    var endpoint = configuration["Ai:Local:Endpoint"] ?? "http://localhost:11434";
    var requiredModels = new[]
    {
        configuration["Ai:Local:ChatModel"] ?? "llama3.2:3b",
        configuration["Ai:Local:EmbeddingModel"] ?? "nomic-embed-text:latest"
    }
    .Where(model => !string.IsNullOrWhiteSpace(model))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

    state.MarkRunning("Checking local Ollama models", requiredModels.Length);

    try
    {
        await PullMissingModelsAsync(endpoint, requiredModels, state);
        state.MarkCompleted("Local models are ready");
    }
    catch (Exception exception)
    {
        logger.LogWarning(exception, "Local model setup failed.");
        state.MarkFailed(ToClientMessage(exception));
    }
}

static async Task PullMissingModelsAsync(string endpoint, string[] requiredModels, ModelSetupState state)
{
    StartOllamaIfNeeded(endpoint);
    await WaitForOllamaAsync(endpoint, TimeSpan.FromSeconds(12));
    var installedModels = await GetInstalledOllamaModelsAsync(endpoint);
    var missingModels = requiredModels
        .Where(model => !ModelExists(installedModels, model))
        .ToArray();

    if (missingModels.Length == 0)
    {
        state.MarkCompleted("Selected models are already installed");
        return;
    }

    var ollamaPath = FindOllamaExecutable();
    foreach (var model in missingModels)
    {
        state.MarkModel($"Downloading model {model}", model);
        await PullOllamaModelAsync(ollamaPath, model, state);
        state.MarkModelDone(model);
    }
}

static async Task InstallOllamaAsync(string appDataRoot, ModelSetupState state)
{
    if (!OperatingSystem.IsWindows())
    {
        throw new InvalidOperationException("Automatic Ollama installation from this EXE is currently supported on Windows only.");
    }

    var existing = TryFindOllamaExecutable();
    if (!string.IsNullOrWhiteSpace(existing))
    {
        state.UpdateMessage("Ollama is already installed. Starting Ollama...");
        StartOllamaProcess(existing);
        await WaitForOllamaAsync("http://localhost:11434", TimeSpan.FromSeconds(20));
        return;
    }

    var setupDir = Path.Combine(appDataRoot, "setup");
    Directory.CreateDirectory(setupDir);
    var installerPath = Path.Combine(setupDir, "OllamaSetup.exe");

    state.UpdateMessage("Downloading Ollama installer...");
    await DownloadFileAsync("https://ollama.com/download/OllamaSetup.exe", installerPath, state);

    state.UpdateMessage("Running Ollama installer. Approve Windows prompts if shown.");
    var installerStart = new ProcessStartInfo
    {
        FileName = installerPath,
        Arguments = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART",
        UseShellExecute = true
    };
    if (!IsAdministrator())
    {
        installerStart.Verb = "runas";
    }

    using var installer = Process.Start(installerStart)
        ?? throw new InvalidOperationException("Failed to start Ollama installer.");

    await installer.WaitForExitAsync();

    var deadline = DateTimeOffset.UtcNow.AddSeconds(60);
    while (DateTimeOffset.UtcNow < deadline)
    {
        var ollamaPath = TryFindOllamaExecutable();
        if (!string.IsNullOrWhiteSpace(ollamaPath))
        {
            state.UpdateMessage("Starting Ollama...");
            StartOllamaProcess(ollamaPath);
            await WaitForOllamaAsync("http://localhost:11434", TimeSpan.FromSeconds(30));
            return;
        }

        await Task.Delay(1000);
    }

    throw new InvalidOperationException("Ollama installer finished, but ollama.exe was not found.");
}

static async Task DownloadFileAsync(string url, string outputPath, ModelSetupState state)
{
    using var httpClient = new HttpClient();
    using var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
    response.EnsureSuccessStatusCode();

    var totalBytes = response.Content.Headers.ContentLength;
    await using var source = await response.Content.ReadAsStreamAsync();
    await using var destination = File.Create(outputPath);

    var buffer = new byte[1024 * 128];
    long totalRead = 0;

    while (true)
    {
        var read = await source.ReadAsync(buffer);
        if (read == 0)
        {
            break;
        }

        await destination.WriteAsync(buffer.AsMemory(0, read));
        totalRead += read;

        if (totalBytes is > 0)
        {
            var percent = Math.Round(totalRead * 100d / totalBytes.Value);
            state.UpdateMessage($"Downloading Ollama installer: {percent}% ({FormatBytes(totalRead)} / {FormatBytes(totalBytes.Value)})");
        }
        else
        {
            state.UpdateMessage($"Downloading Ollama installer: {FormatBytes(totalRead)}");
        }
    }
}

static string FormatBytes(long bytes)
{
    if (bytes >= 1024L * 1024L * 1024L)
    {
        return $"{bytes / 1024d / 1024d / 1024d:F1} GB";
    }

    if (bytes >= 1024L * 1024L)
    {
        return $"{bytes / 1024d / 1024d:F1} MB";
    }

    if (bytes >= 1024L)
    {
        return $"{bytes / 1024d:F1} KB";
    }

    return $"{bytes} B";
}

static bool IsAdministrator()
{
    if (!OperatingSystem.IsWindows())
    {
        return false;
    }

    try
    {
        var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var principal = new System.Security.Principal.WindowsPrincipal(identity);
        return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
    }
    catch
    {
        return false;
    }
}

static void StartOllamaIfNeeded(string endpoint)
{
    if (!endpoint.Contains("localhost", StringComparison.OrdinalIgnoreCase)
        && !endpoint.Contains("127.0.0.1", StringComparison.OrdinalIgnoreCase))
    {
        return;
    }

    var ollamaPath = TryFindOllamaExecutable();
    if (!string.IsNullOrWhiteSpace(ollamaPath))
    {
        StartOllamaProcess(ollamaPath);
    }
}

static void StartOllamaProcess(string ollamaPath)
{
    try
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = ollamaPath,
            Arguments = "serve",
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }
    catch
    {
        // Ollama may already be running from its tray app.
    }
}

static async Task WaitForOllamaAsync(string endpoint, TimeSpan timeout)
{
    using var cancellation = new CancellationTokenSource(timeout);
    using var httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(2)
    };

    while (!cancellation.IsCancellationRequested)
    {
        try
        {
            using var response = await httpClient.GetAsync($"{endpoint.TrimEnd('/')}/api/tags", cancellation.Token);
            if (response.IsSuccessStatusCode)
            {
                return;
            }
        }
        catch
        {
            await Task.Delay(1000, CancellationToken.None);
        }
    }

    throw new InvalidOperationException($"Ollama is not reachable at {endpoint}. Install/start Ollama first.");
}

static async Task<string[]> GetInstalledOllamaModelsAsync(string endpoint)
{
    using var httpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(5)
    };

    var tagsJson = await httpClient.GetStringAsync($"{endpoint.TrimEnd('/')}/api/tags");
    return ReadOllamaModels(tagsJson);
}

static string FindOllamaExecutable()
{
    var found = TryFindOllamaExecutable();
    if (!string.IsNullOrWhiteSpace(found))
    {
        return found;
    }

    return "ollama";
}

static string? TryFindOllamaExecutable()
{
    if (OperatingSystem.IsWindows())
    {
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "Ollama", "ollama.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Ollama", "ollama.exe")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    return null;
}

static async Task PullOllamaModelAsync(string ollamaPath, string model, ModelSetupState state)
{
    using var process = new Process();
    process.StartInfo = new ProcessStartInfo
    {
        FileName = ollamaPath,
        RedirectStandardError = true,
        RedirectStandardOutput = true,
        UseShellExecute = false,
        CreateNoWindow = true,
        StandardOutputEncoding = Encoding.UTF8,
        StandardErrorEncoding = Encoding.UTF8
    };
    process.StartInfo.ArgumentList.Add("pull");
    process.StartInfo.ArgumentList.Add(model);

    process.OutputDataReceived += (_, eventArgs) =>
    {
        if (!string.IsNullOrWhiteSpace(eventArgs.Data))
        {
            state.UpdateMessage(CleanOllamaOutput(eventArgs.Data));
        }
    };
    process.ErrorDataReceived += (_, eventArgs) =>
    {
        if (!string.IsNullOrWhiteSpace(eventArgs.Data))
        {
            state.UpdateMessage(CleanOllamaOutput(eventArgs.Data));
        }
    };

    try
    {
        process.Start();
    }
    catch (Exception exception)
    {
        throw new InvalidOperationException("Ollama is not installed or is not visible in PATH.", exception);
    }

    process.BeginOutputReadLine();
    process.BeginErrorReadLine();
    await process.WaitForExitAsync();

    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"ollama pull {model} failed with exit code {process.ExitCode}.");
    }
}

static string CleanOllamaOutput(string output)
{
    var cleaned = Regex.Replace(output, @"\x1B\[[0-?]*[ -/]*[@-~]", string.Empty);
    cleaned = Regex.Replace(cleaned, @"[^\u0009\u000A\u000D\u0020-\u007E\u0400-\u04FF%./:_-]+", " ");
    cleaned = Regex.Replace(cleaned, @"\s{2,}", " ").Trim();

    return string.IsNullOrWhiteSpace(cleaned)
        ? "Downloading model..."
        : cleaned;
}

static ModelPreset[] GetModelPresets()
{
    return
    [
        new("Fast", "llama3.2:3b", "nomic-embed-text:latest", "Small and fast for most PCs"),
        new("Quality", "llama3.1:8b", "nomic-embed-text:latest", "Better answers, slower and heavier"),
        new("Use Installed", "", "nomic-embed-text:latest", "Pick an already installed chat model")
    ];
}

static void WriteUserModelSettings(string userSettingsPath, string chatModel, string embeddingModel)
{
    var settings = new
    {
        Ai = new
        {
            ActiveProvider = "Local",
            Local = new
            {
                Endpoint = "http://localhost:11434",
                ChatModel = chatModel,
                EmbeddingModel = embeddingModel
            }
        }
    };

    var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
    {
        WriteIndented = true
    });
    File.WriteAllText(userSettingsPath, json, Encoding.UTF8);
}

static void RestartApplication(bool openBrowser)
{
    var processPath = Environment.ProcessPath;
    if (string.IsNullOrWhiteSpace(processPath) || !File.Exists(processPath))
    {
        return;
    }

    var arguments = openBrowser ? "" : "--no-open";
    Process.Start(new ProcessStartInfo
    {
        FileName = processPath,
        Arguments = arguments,
        UseShellExecute = true,
        WorkingDirectory = AppContext.BaseDirectory
    });
}

public sealed record AskRequest(string Question);
public sealed record DependencyIssue(string Title, string Detail);
public sealed record ModelPreset(string Name, string ChatModel, string EmbeddingModel, string Description);
public sealed record ModelSelectionRequest(string ChatModel, string? EmbeddingModel);

public sealed class ModelSetupState
{
    private readonly object _gate = new();

    public bool IsRunning { get; private set; }
    public bool IsCompleted { get; private set; }
    public bool HasError { get; private set; }
    public string Message { get; private set; } = "Not started";
    public string? CurrentModel { get; private set; }
    public int Current { get; private set; }
    public int Total { get; private set; }

    public void MarkSkipped(string message)
    {
        lock (_gate)
        {
            IsRunning = false;
            IsCompleted = true;
            HasError = false;
            Message = message;
        }
    }

    public void MarkRunning(string message, int total)
    {
        lock (_gate)
        {
            IsRunning = true;
            IsCompleted = false;
            HasError = false;
            Message = message;
            Total = total;
        }
    }

    public void MarkModel(string message, string model)
    {
        lock (_gate)
        {
            IsRunning = true;
            Message = message;
            CurrentModel = model;
        }
    }

    public void MarkModelDone(string model)
    {
        lock (_gate)
        {
            Current++;
            CurrentModel = model;
            Message = $"Installed model {model}";
        }
    }

    public void UpdateMessage(string message)
    {
        lock (_gate)
        {
            Message = message;
        }
    }

    public void MarkCompleted(string message)
    {
        lock (_gate)
        {
            IsRunning = false;
            IsCompleted = true;
            HasError = false;
            Message = message;
            CurrentModel = null;
        }
    }

    public void MarkFailed(string message)
    {
        lock (_gate)
        {
            IsRunning = false;
            IsCompleted = false;
            HasError = true;
            Message = message;
        }
    }
}
