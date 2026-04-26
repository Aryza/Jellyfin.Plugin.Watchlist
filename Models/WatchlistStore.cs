namespace Jellyfin.Plugin.Watchlist.Models;

public class WatchlistStore
{
    /// <summary>userId (Guid.ToString("N")) → ordered list of watchlist entries.</summary>
    public Dictionary<string, List<WatchlistEntry>> Entries { get; set; } = new();
}
