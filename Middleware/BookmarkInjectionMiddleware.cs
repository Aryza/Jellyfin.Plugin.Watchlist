// Middleware/BookmarkInjectionMiddleware.cs
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Watchlist.Middleware;

/// <summary>
/// Injects the watchlist bookmark script tag into Jellyfin Web's index.html.
///
/// Strategies (in order):
///   1. Direct — resolves index.html relative to WebRootPath and serves the
///      modified file directly (works for standard on-premise Jellyfin installs).
///   2. Buffer — captures the response body from the pipeline as a fallback.
///
/// Both strategies set Cache-Control: no-store so the injected page is never
/// cached at CDN/proxy layers.
///
/// Pattern mirrors BORNIOS/JellyTrend's ScriptInjectionMiddleware.
/// </summary>
public sealed class BookmarkInjectionMiddleware : IMiddleware
{
    private const string ScriptTag =
        "\n    <script src=\"/Watchlist/watchlist.js?v=1.0.23.0\" defer></script>";

    private const string Marker = "/Watchlist/watchlist.js";

    private readonly IWebHostEnvironment                  _env;
    private readonly ILogger<BookmarkInjectionMiddleware> _logger;

    public BookmarkInjectionMiddleware(IWebHostEnvironment env, ILogger<BookmarkInjectionMiddleware> logger)
    {
        _env    = env;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, RequestDelegate next)
    {
        if (!ShouldIntercept(context))
        {
            await next(context);
            return;
        }

        var indexPath = ResolveIndexHtmlPath();
        if (indexPath is not null)
        {
            await ServeDirectAsync(context, indexPath);
            return;
        }

        await ServeBufferedAsync(context, next);
    }

    private string? ResolveIndexHtmlPath()
    {
        var webRoot = _env.WebRootPath;
        if (string.IsNullOrEmpty(webRoot)) return null;

        var baseDir = Directory.GetParent(webRoot)?.FullName;
        if (baseDir is null) return null;

        foreach (var subdir in new[] { "jellyfin-web", "web" })
        {
            var candidate = Path.Combine(baseDir, subdir, "index.html");
            if (File.Exists(candidate)) return candidate;
        }

        var direct = Path.Combine(webRoot, "index.html");
        return File.Exists(direct) ? direct : null;
    }

    private async Task ServeDirectAsync(HttpContext context, string indexPath)
    {
        var html = await File.ReadAllTextAsync(indexPath);

        if (!html.Contains(Marker, StringComparison.Ordinal)
            && html.Contains("</head>", StringComparison.OrdinalIgnoreCase))
        {
            html = html.Replace("</head>", ScriptTag + "\n</head>", StringComparison.OrdinalIgnoreCase);
            _logger.LogInformation("Watchlist: script injected into '{P}'.", indexPath);
        }

        var bytes = Encoding.UTF8.GetBytes(html);
        context.Response.StatusCode    = 200;
        context.Response.ContentType   = "text/html; charset=utf-8";
        context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
        context.Response.Headers["Pragma"]        = "no-cache";
        context.Response.Headers["Expires"]       = "0";
        context.Response.ContentLength = bytes.Length;
        await context.Response.Body.WriteAsync(bytes);
    }

    private async Task ServeBufferedAsync(HttpContext context, RequestDelegate next)
    {
        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        await next(context);

        context.Response.Body = originalBody;
        buffer.Seek(0, SeekOrigin.Begin);

        var contentType = context.Response.ContentType ?? string.Empty;
        var isHtml      = contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase);

        // If a downstream middleware (e.g. FileTransformation) already encoded the
        // body, we cannot safely modify it — pass through untouched.
        if (!isHtml || context.Response.Headers.ContainsKey("Content-Encoding"))
        {
            await buffer.CopyToAsync(originalBody);
            return;
        }

        var html = await new StreamReader(buffer, Encoding.UTF8).ReadToEndAsync();

        if (html.Length == 0)
        {
            _logger.LogWarning("Watchlist: empty buffer — static-file middleware did not pass through the stream");
            return;
        }

        if (!html.Contains(Marker, StringComparison.Ordinal)
            && html.Contains("</head>", StringComparison.OrdinalIgnoreCase))
        {
            html = html.Replace("</head>", ScriptTag + "\n</head>", StringComparison.OrdinalIgnoreCase);
            _logger.LogInformation("Watchlist: script injected (buffer strategy).");
        }

        var bytes = Encoding.UTF8.GetBytes(html);
        if (!context.Response.HasStarted)
        {
            context.Response.Headers["Cache-Control"] = "no-store, no-cache, must-revalidate, max-age=0";
        context.Response.Headers["Pragma"]        = "no-cache";
        context.Response.Headers["Expires"]       = "0";
            context.Response.ContentLength            = bytes.Length;
        }

        await originalBody.WriteAsync(bytes);
    }

    private static bool ShouldIntercept(HttpContext context)
    {
        if (!HttpMethods.IsGet(context.Request.Method)) return false;

        var path = context.Request.Path.Value ?? string.Empty;
        return path.Equals("/web/index.html", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/web/",           StringComparison.OrdinalIgnoreCase)
            || path.Equals("/",               StringComparison.OrdinalIgnoreCase);
    }
}
