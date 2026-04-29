// Web/IndexHtmlTransformer.cs
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Watchlist.Web;

/// <summary>
/// Callback invoked by IAmParadox27's File Transformation plugin.
/// Receives index.html contents, returns the modified HTML with our bookmark
/// script tag injected before &lt;/body&gt;.
///
/// Must be static (FileTransformation invokes via reflection) and take exactly
/// one parameter FileTransformation can deserialise from the {"contents":"..."}
/// JObject it constructs.
/// </summary>
public static class IndexHtmlTransformer
{
    public sealed class Payload
    {
        /// <summary>Current file contents.</summary>
        public string Contents { get; set; } = string.Empty;
    }

    private const string Marker    = "watchlist-plugin-marker";
    private const string Injection =
        "    <meta name=\"watchlist-plugin-marker\" content=\"1.0.29.0\">\n" +
        "    <script src=\"/Watchlist/watchlist.js?v=1.0.29.0\" defer></script>\n";

    private static int     _invocationCount;
    private static ILogger? _logger;
    private static string? _diagFilePath;

    /// <summary>Number of times File Transformation has invoked our callback.</summary>
    public static int InvocationCount => _invocationCount;

    /// <summary>Hooked up at startup by FileTransformationRegistrar so we can log via Jellyfin's logger.</summary>
    public static void SetLogger(ILogger logger) => _logger = logger;

    /// <summary>Set at startup to a writeable path; Transform appends to it on every call.</summary>
    public static void SetDiagFile(string path) => _diagFilePath = path;

    /// <summary>
    /// File Transformation entry point. Returns the (possibly) modified HTML.
    /// </summary>
    public static string Transform(Payload payload)
    {
        var n    = System.Threading.Interlocked.Increment(ref _invocationCount);
        var html = payload?.Contents ?? string.Empty;

        // File-based proof of invocation (bypasses logger configuration entirely).
        if (_diagFilePath is not null)
        {
            try
            {
                System.IO.File.AppendAllText(_diagFilePath,
                    $"{System.DateTime.UtcNow:o}\tcall #{n}\tlength={html.Length}\n");
            }
            catch { /* best effort */ }
        }

        _logger?.LogInformation(
            "Watchlist: IndexHtmlTransformer.Transform invoked (call #{Count}, contents length={Length})",
            n, html.Length);

        if (string.IsNullOrEmpty(html)) return html;

        // Idempotent: skip if already injected (works even if File Transformation
        // calls our callback multiple times for the same response).
        if (html.Contains(Marker, System.StringComparison.Ordinal)) return html;

        if (html.Contains("</body>", System.StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogInformation("Watchlist: injected script + meta tag before </body> (call #{Count})", n);
            return html.Replace("</body>", Injection + "</body>", System.StringComparison.OrdinalIgnoreCase);
        }

        if (html.Contains("</head>", System.StringComparison.OrdinalIgnoreCase))
        {
            _logger?.LogInformation("Watchlist: injected script + meta tag before </head> (call #{Count})", n);
            return html.Replace("</head>", Injection + "</head>", System.StringComparison.OrdinalIgnoreCase);
        }

        _logger?.LogWarning("Watchlist: index.html had no </body> or </head> — could not inject");
        return html;
    }
}
