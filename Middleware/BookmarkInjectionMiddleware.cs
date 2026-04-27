// Middleware/BookmarkInjectionMiddleware.cs
using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Watchlist.Middleware;

/// <summary>
/// Intercepts GET /web/index.html and injects the watchlist bookmark script.
/// Primary path: serve index.html directly from disk with script injected.
/// Fallback path: buffer the downstream response and rewrite on the fly.
/// Handles gzip/deflate/br compressed responses (e.g. from FileTransformation plugin).
/// Implements <see cref="IMiddleware"/> so ASP.NET resolves it from DI per request
/// (the same pattern that worked in MissingMediaChecker's ScriptInjectionMiddleware).
/// </summary>
public sealed class BookmarkInjectionMiddleware : IMiddleware
{
    private const string Marker    = "data-plugin=\"watchlist\"";
    private const string ScriptTag = "\n    <script data-plugin=\"watchlist\" src=\"/Watchlist/inject.js\" defer></script>";

    private readonly IWebHostEnvironment                   _env;
    private readonly ILogger<BookmarkInjectionMiddleware>  _logger;

    public BookmarkInjectionMiddleware(
        IWebHostEnvironment env,
        ILogger<BookmarkInjectionMiddleware> logger)
    {
        _env    = env;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (!ShouldIntercept(context))
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        _logger.LogDebug("Watchlist: intercepting index.html request at {Path}", context.Request.Path);

        var indexPath = ResolveIndexHtmlPath();
        if (indexPath is not null)
        {
            _logger.LogDebug("Watchlist: serving patched index.html from {IndexPath}", indexPath);
            await ServeDirectAsync(context, indexPath).ConfigureAwait(false);
            return;
        }

        _logger.LogDebug("Watchlist: index.html not found on disk; falling back to response buffering");
        await ServeBufferedAsync(context, next).ConfigureAwait(false);
    }

    private static bool ShouldIntercept(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method)) return false;

        var path = context.Request.Path.Value ?? string.Empty;
        return path == "/"
            || path.Equals("/index.html",      StringComparison.OrdinalIgnoreCase)
            || path.Equals("/web/index.html",  StringComparison.OrdinalIgnoreCase)
            || path.Equals("/web/",            StringComparison.OrdinalIgnoreCase)
            || path.EndsWith("/index.html",    StringComparison.OrdinalIgnoreCase);
    }

    private string? ResolveIndexHtmlPath()
    {
        var webRoot = _env.WebRootPath;
        if (string.IsNullOrEmpty(webRoot) || !Directory.Exists(webRoot)) return null;

        var baseDir = Directory.GetParent(webRoot)?.FullName;
        if (baseDir is not null)
        {
            foreach (var subdir in new[] { "jellyfin-web", "web" })
            {
                var candidate = Path.Combine(baseDir, subdir, "index.html");
                if (File.Exists(candidate)) return candidate;
            }
        }

        var direct = Path.Combine(webRoot, "index.html");
        return File.Exists(direct) ? direct : null;
    }

    private async Task ServeDirectAsync(HttpContext context, string indexPath)
    {
        try
        {
            var html = await File.ReadAllTextAsync(indexPath, context.RequestAborted).ConfigureAwait(false);

            if (!html.Contains(Marker, StringComparison.Ordinal)
                && html.Contains("</body>", StringComparison.OrdinalIgnoreCase))
            {
                html = html.Replace("</body>", ScriptTag + "\n</body>", StringComparison.OrdinalIgnoreCase);
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers["Cache-Control"] = "no-store";
            await context.Response.WriteAsync(html, context.RequestAborted).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Watchlist: direct index.html injection failed, falling back to buffered path");
            // Let it fall through — caller will try buffered path next time or
            // the response is already partially written; swallow the error gracefully.
        }
    }

    private async Task ServeBufferedAsync(HttpContext context, RequestDelegate next)
    {
        var original = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        try
        {
            await next(context).ConfigureAwait(false);

            context.Response.Body = original;
            buffer.Position = 0;

            var ct       = context.Response.ContentType ?? string.Empty;
            var encoding = context.Response.Headers.ContentEncoding.ToString();

            if (!ct.Contains("text/html", StringComparison.OrdinalIgnoreCase))
            {
                await buffer.CopyToAsync(original, context.RequestAborted).ConfigureAwait(false);
                return;
            }

            Stream readStream = encoding switch
            {
                var e when e.Contains("gzip",    StringComparison.OrdinalIgnoreCase) => new GZipStream(buffer,    CompressionMode.Decompress, leaveOpen: true),
                var e when e.Contains("deflate", StringComparison.OrdinalIgnoreCase) => new DeflateStream(buffer, CompressionMode.Decompress, leaveOpen: true),
                var e when e.Contains("br",      StringComparison.OrdinalIgnoreCase) => new BrotliStream(buffer,  CompressionMode.Decompress, leaveOpen: true),
                _                                                                     => buffer
            };

            string html;
            using (readStream)
            using (var reader = new StreamReader(readStream, leaveOpen: false))
                html = await reader.ReadToEndAsync(context.RequestAborted).ConfigureAwait(false);

            if (!html.Contains(Marker, StringComparison.Ordinal)
                && html.Contains("</body>", StringComparison.OrdinalIgnoreCase))
            {
                html = html.Replace("</body>", ScriptTag + "\n</body>", StringComparison.OrdinalIgnoreCase);
            }

            context.Response.Headers.Remove("Content-Encoding");
            context.Response.Headers["Cache-Control"] = "no-store";
            context.Response.ContentLength = null;
            await context.Response.WriteAsync(html, context.RequestAborted).ConfigureAwait(false);
        }
        finally
        {
            context.Response.Body = original;
        }
    }
}
