// Middleware/IndexHtmlPatcher.cs
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Watchlist.Middleware;

/// <summary>
/// Patches Jellyfin Web's index.html on disk at startup to inject the watchlist
/// bookmark script tag. Runs independently of ASP.NET middleware pipeline ordering,
/// so it works even if IStartupFilter registration is unreliable in Jellyfin's
/// plugin loading sequence.
/// </summary>
public sealed class IndexHtmlPatcher : IHostedService
{
    // Unique marker used to detect existing injection (idempotent).
    private const string Marker    = "data-plugin=\"watchlist\"";
    private const string ScriptTag = "    <script data-plugin=\"watchlist\" src=\"/Watchlist/inject.js\" defer></script>\n";

    private readonly IWebHostEnvironment        _env;
    private readonly ILogger<IndexHtmlPatcher>  _logger;

    public IndexHtmlPatcher(IWebHostEnvironment env, ILogger<IndexHtmlPatcher> logger)
    {
        _env    = env;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken ct)
    {
        var indexPath = FindIndexHtml();
        if (indexPath is null)
        {
            _logger.LogWarning("Watchlist: could not locate index.html; bookmark icons require the script injection middleware to work");
            return;
        }

        try
        {
            var html = await File.ReadAllTextAsync(indexPath, ct).ConfigureAwait(false);

            if (html.Contains(Marker, StringComparison.Ordinal))
            {
                _logger.LogDebug("Watchlist: index.html already patched at {Path}", indexPath);
                return;
            }

            if (!html.Contains("</body>", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning("Watchlist: index.html at {Path} has no </body> — cannot inject", indexPath);
                return;
            }

            html = html.Replace(
                "</body>",
                ScriptTag + "</body>",
                StringComparison.OrdinalIgnoreCase);

            await File.WriteAllTextAsync(indexPath, html, Encoding.UTF8, ct).ConfigureAwait(false);
            _logger.LogInformation("Watchlist: injected bookmark script into {Path}", indexPath);
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Watchlist: no write permission for index.html at {Path}; bookmark icons will not appear unless the middleware injection works", indexPath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Watchlist: failed to patch index.html at {Path}", indexPath);
        }
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private string? FindIndexHtml()
    {
        // 1. Paths derived from IWebHostEnvironment.WebRootPath
        var webRoot = _env.WebRootPath;
        if (!string.IsNullOrEmpty(webRoot) && Directory.Exists(webRoot))
        {
            var baseDir = Directory.GetParent(webRoot)?.FullName;
            if (baseDir is not null)
            {
                foreach (var sub in new[] { "jellyfin-web", "web" })
                {
                    var c = Path.Combine(baseDir, sub, "index.html");
                    if (File.Exists(c)) return c;
                }
            }

            var direct = Path.Combine(webRoot, "index.html");
            if (File.Exists(direct)) return direct;
        }

        // 2. Well-known paths for common Jellyfin installs
        foreach (var path in WellKnownPaths())
        {
            if (File.Exists(path)) return path;
        }

        return null;
    }

    private static IEnumerable<string> WellKnownPaths()
    {
        // Linux / Debian package
        yield return "/usr/share/jellyfin/web/index.html";
        yield return "/usr/lib/jellyfin-web/index.html";
        // Docker official images
        yield return "/jellyfin/jellyfin-web/index.html";
        yield return "/app/jellyfin-web/index.html";
        // macOS Homebrew
        yield return "/opt/homebrew/share/jellyfin/web/index.html";
        // Windows default
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrEmpty(pf))
            yield return Path.Combine(pf, "Jellyfin", "Server", "jellyfin-web", "index.html");
    }
}
