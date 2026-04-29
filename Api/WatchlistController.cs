// Api/WatchlistController.cs
using System.Reflection;
using Jellyfin.Plugin.Watchlist.Services;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Net;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Watchlist.Api;

[ApiController]
[Route("Watchlist")]
[AllowAnonymous]
public class WatchlistController : ControllerBase
{
    private readonly WatchlistService             _watchlist;
    private readonly ILibraryManager              _library;
    private readonly ILogger<WatchlistController> _logger;
    private readonly IAuthorizationContext        _authContext;

    public WatchlistController(
        WatchlistService watchlist,
        ILibraryManager library,
        ILogger<WatchlistController> logger,
        IAuthorizationContext authContext)
    {
        _watchlist   = watchlist;
        _library     = library;
        _logger      = logger;
        _authContext = authContext;
    }

    // ── GET /Watchlist/Items ─────────────────────────────────────────────────
    [HttpGet("Items")]
    public async Task<ActionResult> GetItems()
    {
        var userId = await GetUserIdAsync().ConfigureAwait(false);
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
    [HttpPost("Items/{itemId}")]
    public async Task<ActionResult> AddItem(Guid itemId)
    {
        var userId = await GetUserIdAsync().ConfigureAwait(false);
        if (userId == Guid.Empty) return Unauthorized();

        if (_watchlist.Contains(userId, itemId))
            return Conflict(new { message = "Already in watchlist." });

        var item = _library.GetItemById(itemId);
        if (item is null) return NotFound(new { message = "Item not found in library." });

        _watchlist.Add(userId, itemId, item.GetBaseItemKind().ToString());
        return Ok(new { inWatchlist = true });
    }

    // ── DELETE /Watchlist/Items/{itemId} ─────────────────────────────────────
    [HttpDelete("Items/{itemId}")]
    public async Task<ActionResult> RemoveItem(Guid itemId)
    {
        var userId = await GetUserIdAsync().ConfigureAwait(false);
        if (userId == Guid.Empty) return Unauthorized();

        if (!_watchlist.Remove(userId, itemId))
            return NotFound(new { message = "Not in watchlist." });

        return Ok(new { inWatchlist = false });
    }

    // ── GET /Watchlist/Status/{itemId} ───────────────────────────────────────
    [HttpGet("Status/{itemId}")]
    public async Task<ActionResult> GetStatus(Guid itemId)
    {
        var userId = await GetUserIdAsync().ConfigureAwait(false);
        if (userId == Guid.Empty) return Unauthorized();

        return Ok(new { inWatchlist = _watchlist.Contains(userId, itemId) });
    }

    // ── GET /Watchlist/watchlist.js ──────────────────────────────────────────
    [HttpGet("watchlist.js")]
    public IActionResult GetScript()
    {
        const string resourceName = "Jellyfin.Plugin.Watchlist.Web.watchlist.js";
        var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName);
        if (stream is null)
        {
            _logger.LogWarning("Watchlist: embedded resource '{Name}' not found.", resourceName);
            return NotFound();
        }

        return File(stream, "application/javascript");
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private async Task<Guid> GetUserIdAsync()
    {
        var rawHeader = Request.Headers["X-Emby-Authorization"].ToString();
        _logger.LogInformation(
            "Watchlist auth: header present={Present}, preview='{Preview}'",
            rawHeader.Length > 0,
            rawHeader.Length > 60 ? rawHeader[..60] : rawHeader);

        try
        {
            var info = await _authContext.GetAuthorizationInfo(Request).ConfigureAwait(false);
            _logger.LogInformation(
                "Watchlist auth: IsAuthenticated={IsAuth}, UserId={UserId}",
                info.IsAuthenticated, info.UserId);
            return info.IsAuthenticated ? info.UserId : Guid.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Watchlist auth: IAuthorizationContext threw");
            return Guid.Empty;
        }
    }
}
