// Web/IndexHtmlTransformer.cs
namespace Jellyfin.Plugin.Watchlist.Web;

/// <summary>
/// Callback invoked by IAmParadox27's File Transformation plugin.
/// Receives index.html contents, returns the modified HTML with our
/// bookmark script tag injected before &lt;/body&gt;.
///
/// Must be static (FileTransformation invokes via reflection on null instance)
/// and take exactly one parameter that File Transformation can deserialise from
/// the {"contents":"..."} JObject it constructs.
/// </summary>
public static class IndexHtmlTransformer
{
    public sealed class Payload
    {
        /// <summary>Current file contents.</summary>
        public string Contents { get; set; } = string.Empty;
    }

    private const string Marker    = "/Watchlist/watchlist.js";
    private const string ScriptTag = "    <script src=\"/Watchlist/watchlist.js\" defer></script>\n";

    /// <summary>
    /// File Transformation entry point. Returns the (possibly) modified HTML.
    /// </summary>
    public static string Transform(Payload payload)
    {
        var html = payload?.Contents ?? string.Empty;
        if (string.IsNullOrEmpty(html)) return html;

        // Idempotent: skip if already injected.
        if (html.Contains(Marker, System.StringComparison.Ordinal)) return html;

        // Inject before </body> — matches what other IAmParadox27 plugins do.
        if (html.Contains("</body>", System.StringComparison.OrdinalIgnoreCase))
        {
            return html.Replace("</body>", ScriptTag + "</body>", System.StringComparison.OrdinalIgnoreCase);
        }

        // Fallback: try </head>
        if (html.Contains("</head>", System.StringComparison.OrdinalIgnoreCase))
        {
            return html.Replace("</head>", ScriptTag + "</head>", System.StringComparison.OrdinalIgnoreCase);
        }

        return html;
    }
}
