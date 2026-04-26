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

    // ── Helpers ──────────────────────────────────────────────────────────────

    private Guid GetUserId()
    {
        var idStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(idStr, out var id) ? id : Guid.Empty;
    }
}
