using System.Reflection;

namespace CorporateRag;

public static class EmbeddedWebRoot
{
    public static string EnsureExtracted(string contentRootPath)
    {
        var versionKey = GetExtractionVersionKey();
        var webRootPath = Path.Combine(contentRootPath, "wwwroot", versionKey);

        Directory.CreateDirectory(webRootPath);

        var assembly = Assembly.GetExecutingAssembly();
        var resourcePrefix = $"{assembly.GetName().Name}.wwwroot.";

        foreach (var resourceName in assembly.GetManifestResourceNames()
                     .Where(name => name.StartsWith(resourcePrefix, StringComparison.Ordinal)))
        {
            var relativeName = resourceName[resourcePrefix.Length..];
            if (relativeName.StartsWith(".", StringComparison.Ordinal))
            {
                continue;
            }

            var outputPath = ToOutputPath(webRootPath, relativeName);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

            using var resource = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Embedded resource not found: {resourceName}");
            using var output = File.Create(outputPath);
            resource.CopyTo(output);
        }

        File.WriteAllText(Path.Combine(webRootPath, ".embedded-ui"), DateTimeOffset.UtcNow.ToString("O"));

        return webRootPath;
    }

    private static string GetExtractionVersionKey()
    {
        var processPath = Environment.ProcessPath;
        if (!string.IsNullOrWhiteSpace(processPath) && File.Exists(processPath))
        {
            return $"embedded-{File.GetLastWriteTimeUtc(processPath).Ticks}";
        }

        return $"embedded-{Assembly.GetExecutingAssembly().GetName().Version}";
    }

    private static string ToOutputPath(string webRootPath, string relativeName)
    {
        var knownExtensions = new[] { ".html", ".css", ".js", ".map", ".json", ".png", ".jpg", ".jpeg", ".svg", ".ico", ".txt" };

        foreach (var extension in knownExtensions)
        {
            if (!relativeName.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var withoutExtension = relativeName[..^extension.Length];
            var pathPart = withoutExtension.Replace('.', Path.DirectorySeparatorChar);
            return Path.Combine(webRootPath, pathPart + extension);
        }

        return Path.Combine(webRootPath, relativeName.Replace('.', Path.DirectorySeparatorChar));
    }
}
