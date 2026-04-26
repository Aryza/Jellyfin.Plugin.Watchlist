# Jellyfin Watchlist Plugin — Design Spec

**Date:** 2026-04-26  
**Version:** 1.0  
**Target ABI:** 10.11.0.0 / .NET 9

---

## Overview

Standalone Jellyfin plugin that lets each user bookmark movies and TV shows into a personal watchlist. Integrates with IAmParadox27's HomeScreenSections and CustomTabs plugins for surface-level access.

---

## Architecture

```
Jellyfin.Plugin.Watchlist/
├── Plugin.cs                          # Entry point, IPlugin
├── PluginServiceRegistrator.cs        # IPluginServiceRegistrator
├── build.yaml
├── Jellyfin.Plugin.Watchlist.csproj
├── Configuration/
│   ├── PluginConfiguration.cs         # HomeRowMaxItems (default 20)
│   └── configPage.html                # Plugin settings UI
├── Models/
│   ├── WatchlistEntry.cs              # { JellyfinItemId, MediaType, AddedAt }
│   └── WatchlistStore.cs              # Dictionary<Guid userId, List<WatchlistEntry>>
├── Services/
│   ├── WatchlistService.cs            # Add/Remove/Get/Contains per user
│   └── WatchlistEventConsumer.cs      # IEventConsumer<UserDataSaveEventArgs>
├── Api/
│   └── WatchlistController.cs         # REST endpoints
├── HomeSection/
│   └── WatchlistHomeSection.cs        # IHomeSection (HomeScreenSections plugin)
└── Middleware/
    └── BookmarkInjectionMiddleware.cs # Injects bookmark button into Jellyfin web UI
```

---

## Data Model

```csharp
public class WatchlistEntry
{
    public Guid   JellyfinItemId { get; set; }
    public string MediaType      { get; set; } = ""; // "Movie" | "Series"
    public DateTimeOffset AddedAt { get; set; }
}
```

**Storage:** `WatchlistStore` holds `Dictionary<Guid, List<WatchlistEntry>>` (userId → entries). Serialised as plugin XML config via Jellyfin's standard `IConfigurationManager.SaveConfiguration` / `GetConfiguration`. No external DB.

---

## API

All endpoints require Jellyfin auth header (`Authorization: MediaBrowser Token=...`). User identity resolved from `HttpContext.User`.

| Method | Route | Description |
|--------|-------|-------------|
| GET | `/Watchlist/Items` | All watchlist entries for current user, ordered by `AddedAt` desc. Returns Jellyfin item metadata. |
| POST | `/Watchlist/Items/{itemId}` | Add item to watchlist. 409 if already present. |
| DELETE | `/Watchlist/Items/{itemId}` | Remove item. 404 if not present. |
| GET | `/Watchlist/Status/{itemId}` | `{ "inWatchlist": bool }` — used by bookmark button on page load. |

---

## Bookmark Button Injection

`BookmarkInjectionMiddleware` intercepts responses for `index.html` (same pattern as MissingMediaChecker's `ScriptInjectionMiddleware`). Handles gzip/deflate/br decompression.

Injected script:
- On `viewshow` of item detail pages, queries `GET /Watchlist/Status/{itemId}` and renders a bookmark button (🔖) adjacent to `.btnFavorite`.
- Toggle click calls `POST` or `DELETE` accordingly, updates button state optimistically.
- Works for both Movies and Series detail pages.
- Button uses Jellyfin's existing icon button CSS classes to match native look.

---

## Auto-Remove on Watched

`WatchlistEventConsumer` implements `IEventConsumer<UserDataSaveEventArgs>`:

```csharp
if (args.UserData.Played && args.UserData.PlayCount > 0)
    WatchlistService.Remove(args.UserId, args.Item.Id);
```

Fires when Jellyfin marks an item as played (manual or auto at 90% threshold). Only removes from the triggering user's watchlist.

---

## Home Screen Row

`WatchlistHomeSection` implements `IHomeSection` from IAmParadox27's HomeScreenSections plugin.

- Returns last N items from user's watchlist ordered by `AddedAt` desc (N = `PluginConfiguration.HomeRowMaxItems`, default 20).
- Row label: "My Watchlist"
- Resolves Jellyfin `BaseItem` objects from stored IDs; silently skips any IDs no longer in library.

---

## Custom Tab

Registered via IAmParadox27's CustomTabs plugin. Tab label: "Watchlist".

The tab page (`configPage.html`-style embedded resource):
1. On load: calls `GET /Watchlist/Items` → receives list of Jellyfin item IDs + metadata.
2. Calls Jellyfin's `ApiClient.getItems({ Ids: [...] })` to fetch full item details.
3. Passes results to `cardBuilder.buildCards()` with same options as the native movies view — renders identical poster grid with play overlay, favourite, and bookmark toggle.
4. Empty state: centred message "Your watchlist is empty. Bookmark items with the 🔖 button on any movie or series."
5. Sort: `AddedAt` desc (newest bookmark first). No additional sort controls in v1.

---

## Plugin Configuration

| Setting | Default | Description |
|---------|---------|-------------|
| `HomeRowMaxItems` | 20 | Max items shown in home screen row |

---

## Dependencies

| Plugin | Role |
|--------|------|
| [HomeScreenSections](https://github.com/IAmParadox27/jellyfin-plugin-home-sections) | Register home row |
| [CustomTabs](https://github.com/IAmParadox27/jellyfin-plugin-custom-tabs) | Register Watchlist tab |

Both are optional at runtime — if not installed, those surfaces are silently absent. Core bookmark + API functionality works standalone.

---

## Out of Scope (v1)

- TMDB search to add items not in library
- Shared / collaborative watchlists
- Priority ordering within watchlist
- "Watched" history (separate from watchlist)
- Mobile app support
