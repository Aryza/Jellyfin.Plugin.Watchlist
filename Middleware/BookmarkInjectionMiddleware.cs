// Middleware/BookmarkInjectionMiddleware.cs
using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Watchlist.Middleware;

/// <summary>
/// Intercepts GET /web/index.html and injects the watchlist bookmark script.
/// Handles gzip/deflate/br compressed responses (e.g. from FileTransformation plugin).
/// </summary>
public sealed class BookmarkInjectionMiddleware
{
    private const string ScriptTag = """
        <script>
        (function () {
            'use strict';

            function getToken() {
                try { return window.ApiClient && window.ApiClient.accessToken ? window.ApiClient.accessToken() : null; } catch { return null; }
            }

            function getItemId() {
                var hash = window.location.hash || '';
                var qs   = hash.indexOf('?') >= 0 ? hash.slice(hash.indexOf('?') + 1) : '';
                var params = new URLSearchParams(qs);
                return params.get('id') || params.get('itemId');
            }

            function authHeader() {
                var token = getToken();
                return token ? { 'Authorization': 'MediaBrowser Token=' + token } : {};
            }

            var _inWatchlist = false;

            function updateBtn(btn, inWl) {
                _inWatchlist    = inWl;
                btn.title       = inWl ? 'Remove from Watchlist' : 'Add to Watchlist';
                btn.style.color = inWl ? 'var(--theme-button-focus-color, #00a4dc)' : '';
            }

            function createBtn() {
                var btn = document.createElement('button');
                btn.className         = 'btnWatchlistToggle paper-icon-button-light';
                btn.type              = 'button';
                btn.innerHTML         = '<span class="material-icons md-18">bookmark</span>';
                btn.style.cssText     = 'cursor:pointer;';
                return btn;
            }

            async function injectButton() {
                var itemId = getItemId();
                if (!itemId) return;

                // Skip non-detail pages
                var hash = window.location.hash || '';
                if (!hash.includes('details') && !hash.includes('item')) return;

                // Wait for favourite button to appear
                var favBtn = document.querySelector('.btnFavorite');
                if (!favBtn || document.querySelector('.btnWatchlistToggle')) return;

                // Check current status
                try {
                    var resp = await fetch('/Watchlist/Status/' + itemId, { headers: authHeader() });
                    if (!resp.ok) return;
                    var data = await resp.json();
                    var btn  = createBtn();
                    updateBtn(btn, data.inWatchlist);

                    btn.addEventListener('click', async function () {
                        var method = _inWatchlist ? 'DELETE' : 'POST';
                        var url    = '/Watchlist/Items/' + itemId;
                        try {
                            var r = await fetch(url, { method: method, headers: authHeader() });
                            if (r.ok) updateBtn(btn, !_inWatchlist);
                        } catch (e) { console.warn('Watchlist toggle error', e); }
                    });

                    favBtn.parentNode.insertBefore(btn, favBtn.nextSibling);
                } catch (e) {
                    console.warn('Watchlist inject error', e);
                }
            }

            // Re-inject on SPA navigation
            var _lastHash = '';
            var observer  = new MutationObserver(function () {
                var h = window.location.hash;
                if (h !== _lastHash) { _lastHash = h; setTimeout(injectButton, 400); }
            });
            document.addEventListener('DOMContentLoaded', function () {
                observer.observe(document.body, { childList: true, subtree: true });
            });
        })();
        </script>
        """;

    private readonly RequestDelegate                      _next;
    private readonly ILogger<BookmarkInjectionMiddleware> _logger;

    public BookmarkInjectionMiddleware(RequestDelegate next, ILogger<BookmarkInjectionMiddleware> logger)
    {
        _next   = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Only intercept index.html GETs
        if (!context.Request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            || !path.EndsWith("index.html", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Buffer the downstream response
        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        await _next(context);

        buffer.Seek(0, SeekOrigin.Begin);
        var encoding = context.Response.Headers.ContentEncoding.ToString();
        string html;

        try
        {
            html = await DecompressAsync(buffer, encoding);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Watchlist: failed to decompress index.html ({Encoding}); skipping injection", encoding);
            buffer.Seek(0, SeekOrigin.Begin);
            context.Response.Body = originalBody;
            await buffer.CopyToAsync(originalBody);
            return;
        }

        if (html.Contains("</body>", StringComparison.OrdinalIgnoreCase))
        {
            html = html.Replace("</body>", ScriptTag + "</body>", StringComparison.OrdinalIgnoreCase);
            context.Response.Headers.Remove("Content-Encoding");
            context.Response.Headers.Remove("Content-Length");
            var bytes = Encoding.UTF8.GetBytes(html);
            context.Response.Body = originalBody;
            context.Response.ContentLength = bytes.Length;
            await originalBody.WriteAsync(bytes);
        }
        else
        {
            buffer.Seek(0, SeekOrigin.Begin);
            context.Response.Body = originalBody;
            await buffer.CopyToAsync(originalBody);
        }
    }

    private static async Task<string> DecompressAsync(Stream stream, string encoding)
    {
        if (string.IsNullOrEmpty(encoding) || encoding.Equals("identity", StringComparison.OrdinalIgnoreCase))
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            return await reader.ReadToEndAsync();
        }

        Stream decompressed = encoding.ToLowerInvariant() switch
        {
            "gzip"    => new GZipStream(stream, CompressionMode.Decompress, leaveOpen: true),
            "deflate" => new DeflateStream(stream, CompressionMode.Decompress, leaveOpen: true),
            "br"      => new BrotliStream(stream, CompressionMode.Decompress, leaveOpen: true),
            _         => stream
        };

        using var r = new StreamReader(decompressed, Encoding.UTF8);
        return await r.ReadToEndAsync();
    }
}
