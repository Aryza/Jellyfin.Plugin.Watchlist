// Services/WatchlistService.cs
using System.Text.Json;
using Jellyfin.Plugin.Watchlist.Models;
using MediaBrowser.Common.Configuration;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Watchlist.Services;

/// <summary>
/// Thread-safe, JSON-backed watchlist store. One entry list per user.
/// All mutations acquire _lock; the on-disk file is rewritten on every change.
/// </summary>
public sealed class WatchlistService
{
    private readonly string                    _storePath;
    private readonly ILogger<WatchlistService> _logger;
    private readonly object                    _lock = new();
    private static readonly JsonSerializerOptions _jsonOptions = new() { WriteIndented = true };

    public WatchlistService(IApplicationPaths paths, ILogger<WatchlistService> logger)
    {
        _storePath = Path.Combine(paths.DataPath, "watchlist_data.json");
        _logger    = logger;
    }

    // ── Public API ────────────────────────────────────────────────────────────

    public List<WatchlistEntry> GetEntries(Guid userId)
    {
        lock (_lock)
        {
            var store = Load();
            return store.Entries.TryGetValue(Key(userId), out var list)
                ? list.OrderByDescending(e => e.AddedAt).ToList()
                : new List<WatchlistEntry>();
        }
    }

    public bool Contains(Guid userId, Guid itemId)
    {
        lock (_lock)
        {
            var store = Load();
            return store.Entries.TryGetValue(Key(userId), out var list)
                && list.Any(e => e.JellyfinItemId == itemId);
        }
    }

    /// <summary>Adds item. No-ops if already present.</summary>
    public void Add(Guid userId, Guid itemId, string mediaType)
    {
        lock (_lock)
        {
            var store = Load();
            var key   = Key(userId);
            if (!store.Entries.TryGetValue(key, out var list))
                store.Entries[key] = list = new List<WatchlistEntry>();

            if (list.Any(e => e.JellyfinItemId == itemId)) return;

            list.Add(new WatchlistEntry
            {
                JellyfinItemId = itemId,
                MediaType      = mediaType,
                AddedAt        = DateTimeOffset.UtcNow
            });
            Save(store);
        }
    }

    /// <summary>Removes item. Returns true if removed, false if not present.</summary>
    public bool Remove(Guid userId, Guid itemId)
    {
        lock (_lock)
        {
            var store = Load();
            var key   = Key(userId);
            if (!store.Entries.TryGetValue(key, out var list)) return false;

            var before = list.Count;
            list.RemoveAll(e => e.JellyfinItemId == itemId);
            if (list.Count != before) { Save(store); return true; }
            return false;
        }
    }

    // ── Private ───────────────────────────────────────────────────────────────

    private static string Key(Guid userId) => userId.ToString("N");

    private WatchlistStore Load()
    {
        try
        {
            if (!File.Exists(_storePath)) return new WatchlistStore();
            var json = File.ReadAllText(_storePath);
            return JsonSerializer.Deserialize<WatchlistStore>(json) ?? new WatchlistStore();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Watchlist: failed to load store from {Path}; starting fresh", _storePath);
            return new WatchlistStore();
        }
    }

    private void Save(WatchlistStore store)
    {
        try
        {
            var json = JsonSerializer.Serialize(store, _jsonOptions);
            File.WriteAllText(_storePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Watchlist: failed to save store to {Path}", _storePath);
        }
    }
}
