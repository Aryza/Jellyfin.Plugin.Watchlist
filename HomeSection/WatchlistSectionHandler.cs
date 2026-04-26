// HomeSection/WatchlistSectionHandler.cs
using Jellyfin.Plugin.Watchlist.Services;
using MediaBrowser.Controller.Dto;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Querying;

namespace Jellyfin.Plugin.Watchlist.HomeSection;

/// <summary>
/// Returns the user's most recently bookmarked items as a home-screen row.
/// Invoked by the Home Screen Sections plugin via reflection.
/// </summary>
public sealed class WatchlistSectionHandler
{
    private readonly WatchlistService _watchlist;
    private readonly ILibraryManager  _library;
    private readonly IUserManager     _userManager;
    private readonly IDtoService      _dto;

    public WatchlistSectionHandler(
        WatchlistService watchlist,
        ILibraryManager library,
        IUserManager userManager,
        IDtoService dto)
    {
        _watchlist   = watchlist;
        _library     = library;
        _userManager = userManager;
        _dto         = dto;
    }

    public QueryResult<BaseItemDto> GetResults(SectionPayload payload)
    {
        var maxItems = Plugin.Instance?.Configuration.HomeRowMaxItems ?? 20;
        var entries  = _watchlist.GetEntries(payload.UserId).Take(maxItems).ToList();

        var items = entries
            .Select(e => _library.GetItemById(e.JellyfinItemId))
            .Where(i => i is not null)
            .ToList()!;

        var user    = _userManager.GetUserById(payload.UserId);
        var options = new DtoOptions
        {
            Fields     = new List<ItemFields> { ItemFields.PrimaryImageAspectRatio, ItemFields.Overview },
            ImageTypeLimit = 1,
            ImageTypes  = new List<ImageType> { ImageType.Primary, ImageType.Thumb, ImageType.Backdrop }
        };

        var dtos = _dto.GetBaseItemDtos(items!, options, user);
        return new QueryResult<BaseItemDto>(null, items.Count, dtos);
    }
}
