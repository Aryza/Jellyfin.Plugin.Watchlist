// Services/WatchlistEventConsumer.cs
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Library;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Watchlist.Services;

/// <summary>
/// Removes an item from the user's watchlist when Jellyfin marks it as played.
/// Fires for both manual "mark as played" and automatic 90%-threshold detection.
/// </summary>
public sealed class WatchlistEventConsumer : IEventConsumer<UserDataSaveEventArgs>
{
    private readonly WatchlistService                    _watchlist;
    private readonly ILogger<WatchlistEventConsumer>    _logger;

    public WatchlistEventConsumer(WatchlistService watchlist, ILogger<WatchlistEventConsumer> logger)
    {
        _watchlist = watchlist;
        _logger    = logger;
    }

    public Task OnEvent(UserDataSaveEventArgs eventArgs)
    {
        if (!eventArgs.UserData.Played) return Task.CompletedTask;

        var userId = eventArgs.UserId;
        var itemId = eventArgs.Item?.Id ?? Guid.Empty;
        if (itemId == Guid.Empty) return Task.CompletedTask;

        if (_watchlist.Remove(userId, itemId))
        {
            _logger.LogInformation(
                "Watchlist: auto-removed item {ItemId} for user {UserId} (marked played)",
                itemId, userId);
        }

        return Task.CompletedTask;
    }
}
