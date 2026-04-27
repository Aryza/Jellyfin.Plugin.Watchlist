// Api/WatchlistController.cs
using System.Security.Claims;
using Jellyfin.Plugin.Watchlist.Services;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jellyfin.Plugin.Watchlist.Api;

[ApiController]
[Route("Watchlist")]
[Authorize]
public class WatchlistController : ControllerBase
{
    private readonly WatchlistService _watchlist;
    private readonly ILibraryManager  _library;

    public WatchlistController(WatchlistService watchlist, ILibraryManager library)
    {
        _watchlist = watchlist;
        _library   = library;
    }

    // ── GET /Watchlist/Items ─────────────────────────────────────────────────
    // Returns entries ordered by AddedAt desc.
    [HttpGet("Items")]
    public ActionResult GetItems()
    {
        var userId = GetUserId();
        if (userId == Guid.Empty) return Unauthorized();

        var entries = _watchlist.GetEntries(userId);
        return Ok(entries.Select(e => new
        {
            jellyfinItemId = e.JellyfinItemId,
            mediaType      = e.MediaType,
            addedAt        = e.AddedAt
        }));
    }

    // ── POST /Watchlist/Items/{itemId} ───────────────────────────────────────
    // Adds item. Resolves mediaType from library. 409 if already present.
    [HttpPost("Items/{itemId}")]
    public ActionResult AddItem(Guid itemId)
    {
        var userId = GetUserId();
        if (userId == Guid.Empty) return Unauthorized();

        if (_watchlist.Contains(userId, itemId))
            return Conflict(new { message = "Already in watchlist." });

        var item = _library.GetItemById(itemId);
        if (item is null) return NotFound(new { message = "Item not found in library." });

        var mediaType = item.GetBaseItemKind().ToString(); // stable enum: Movie, Series, etc.
        _watchlist.Add(userId, itemId, mediaType);
        return Ok(new { inWatchlist = true });
    }

    // ── DELETE /Watchlist/Items/{itemId} ─────────────────────────────────────
    [HttpDelete("Items/{itemId}")]
    public ActionResult RemoveItem(Guid itemId)
    {
        var userId = GetUserId();
        if (userId == Guid.Empty) return Unauthorized();

        if (!_watchlist.Remove(userId, itemId))
            return NotFound(new { message = "Not in watchlist." });

        return Ok(new { inWatchlist = false });
    }

    // ── GET /Watchlist/Status/{itemId} ───────────────────────────────────────
    // Lightweight check; used by bookmark button on page load.
    [HttpGet("Status/{itemId}")]
    public ActionResult GetStatus(Guid itemId)
    {
        var userId = GetUserId();
        if (userId == Guid.Empty) return Unauthorized();

        return Ok(new { inWatchlist = _watchlist.Contains(userId, itemId) });
    }

    // ── GET /Watchlist/inject.js ─────────────────────────────────────────────
    // Bookmark button script injected into Jellyfin Web via middleware.
    // No [Authorize] — loaded before the user logs in.
    [HttpGet("inject.js")]
    [AllowAnonymous]
    public ContentResult GetInjectScript()
    {
        const string script = """
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

                    var hash = window.location.hash || '';
                    if (!hash.includes('details') && !hash.includes('item')) return;

                    var favBtn = document.querySelector('.btnFavorite');
                    if (!favBtn || document.querySelector('.btnWatchlistToggle')) return;

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

                        if (favBtn.parentNode) favBtn.parentNode.insertBefore(btn, favBtn.nextSibling);
                    } catch (e) {
                        console.warn('Watchlist inject error', e);
                    }
                }

                var _lastHash = '';
                var observer  = new MutationObserver(function () {
                    var h = window.location.hash;
                    if (h !== _lastHash) { _lastHash = h; setTimeout(injectButton, 400); }
                });
                document.addEventListener('DOMContentLoaded', function () {
                    observer.observe(document.body, { childList: true, subtree: true });
                    setTimeout(injectButton, 400);
                });
            })();
            """;

        return Content(script, "application/javascript; charset=utf-8");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private Guid GetUserId()
    {
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(idStr, out var id) ? id : Guid.Empty;
    }
}
