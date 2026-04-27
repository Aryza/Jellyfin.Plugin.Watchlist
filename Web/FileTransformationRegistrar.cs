// Web/FileTransformationRegistrar.cs
using System.Runtime.Loader;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.Watchlist.Web;

/// <summary>
/// Registers <see cref="IndexHtmlTransformer.Transform"/> with IAmParadox27's
/// File Transformation plugin (loaded into a separate AssemblyLoadContext, so
/// this is done via reflection — no compile-time dependency).
///
/// Per File Transformation's README: locate its assembly across all load
/// contexts, get the static <c>PluginInterface.RegisterTransformation(JObject)</c>,
/// and invoke it with a payload pointing at our static callback method.
/// </summary>
public sealed class FileTransformationRegistrar : IHostedService
{
    private static readonly Guid TransformationId = new("a1b2c3d4-e5f6-7890-abcd-ef1234567891");

    private readonly ILogger<FileTransformationRegistrar> _logger;

    public FileTransformationRegistrar(ILogger<FileTransformationRegistrar> logger)
    {
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        // Cache the logger inside the static transformer so its callback can log
        // through Jellyfin's logger (Console.WriteLine doesn't reach the log file).
        IndexHtmlTransformer.SetLogger(_logger);

        try
        {
            var ftAssembly = AssemblyLoadContext.All
                .SelectMany(x => x.Assemblies)
                .FirstOrDefault(x => x.FullName?.Contains(".FileTransformation", StringComparison.Ordinal) ?? false);

            if (ftAssembly is null)
            {
                _logger.LogInformation("Watchlist: File Transformation plugin not present; bookmark icons depend on the response-buffering middleware");
                return Task.CompletedTask;
            }

            var pluginInterface = ftAssembly.GetType("Jellyfin.Plugin.FileTransformation.PluginInterface");
            var register        = pluginInterface?.GetMethod("RegisterTransformation");
            if (register is null)
            {
                _logger.LogWarning("Watchlist: File Transformation found but RegisterTransformation method missing");
                return Task.CompletedTask;
            }

            var ourAssembly = typeof(IndexHtmlTransformer).Assembly;
            var payload = new JObject
            {
                ["id"]               = TransformationId.ToString(),
                // Must match the literal pattern other IAmParadox27 plugins use ("index.html",
                // not "index\\.html") — FileTransformation keys pipelines by exact pattern
                // string and prefers direct dict-key matches over regex fallback. Using a
                // different key puts our callback in its own (unreachable) pipeline.
                ["fileNamePattern"]  = "index.html",
                ["callbackAssembly"] = ourAssembly.FullName,
                ["callbackClass"]    = typeof(IndexHtmlTransformer).FullName,
                ["callbackMethod"]   = nameof(IndexHtmlTransformer.Transform)
            };

            register.Invoke(null, new object?[] { payload });

            _logger.LogInformation(
                "Watchlist: registered FileTransformation callback {Class}.{Method} for {Pattern}",
                typeof(IndexHtmlTransformer).FullName,
                nameof(IndexHtmlTransformer.Transform),
                "index.html");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Watchlist: failed to register FileTransformation callback");
        }

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
