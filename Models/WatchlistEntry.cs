namespace Jellyfin.Plugin.Watchlist.Models;

public class WatchlistEntry
{
    public Guid            JellyfinItemId { get; set; }
    public string          MediaType      { get; set; } = string.Empty; // "Movie" | "Series"
    public DateTimeOffset  AddedAt        { get; set; }
}
