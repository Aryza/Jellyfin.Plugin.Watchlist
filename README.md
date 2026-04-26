# Jellyfin Watchlist Plugin

A per-user watchlist for Jellyfin. Bookmark any movie or series from its detail page and access your list from the home screen or a dedicated tab.

## Features

- **Bookmark button** — 🔖 icon injected next to the favorite star on every movie and series detail page
- **Home screen row** — "My Watchlist" row showing your most recently bookmarked items (requires [HomeScreenSections](https://github.com/IAmParadox27/jellyfin-plugin-home-sections))
- **Watchlist tab** — full card-grid view identical to the native movies view (requires [CustomTabs](https://github.com/IAmParadox27/jellyfin-plugin-custom-tabs))
- **Auto-remove on watched** — items are automatically removed when Jellyfin marks them as played

## Requirements

- Jellyfin 10.11 or newer
- [HomeScreenSections plugin](https://github.com/IAmParadox27/jellyfin-plugin-home-sections) *(optional — for home row)*
- [CustomTabs plugin](https://github.com/IAmParadox27/jellyfin-plugin-custom-tabs) *(optional — for Watchlist tab)*

## Installation

### Via plugin catalog

Add the repository URL to your Jellyfin plugin catalog:

```
https://raw.githubusercontent.com/Aryza/Jellyfin.Plugin.Watchlist/main/manifest.json
```

Dashboard → Plugins → Repositories → Add, then install **Watchlist** from the catalog.

### Manual

1. Download `watchlist_<version>.zip` from [Releases](https://github.com/Aryza/Jellyfin.Plugin.Watchlist/releases)
2. Extract `Jellyfin.Plugin.Watchlist.dll` into your Jellyfin plugin directory
3. Restart Jellyfin

## Usage

### Adding items

Navigate to any movie or series detail page. Click the **🔖** bookmark icon next to the favorite star to add it to your watchlist. Click again to remove it.

### Viewing your watchlist

- **Home screen row** — enable "My Watchlist" in your home screen customization (requires HomeScreenSections plugin)
- **Watchlist tab** — appears in the main navigation (requires CustomTabs plugin)

### Settings

Dashboard → Plugins → Watchlist:

| Setting | Default | Description |
|---------|---------|-------------|
| Home screen row — max items | 20 | Maximum items shown in the home-screen row |

## How it works

- Watchlist data is stored per-user as JSON in Jellyfin's data directory (`watchlist_data.json`)
- The bookmark button is injected into Jellyfin's web UI via response middleware (handles gzip/deflate/br compression from other plugins)
- Home row and tab integrations use runtime reflection so neither HomeScreenSections nor CustomTabs is a hard dependency — the plugin works standalone without them

## Building from source

```bash
dotnet publish Jellyfin.Plugin.Watchlist.csproj -c Release -o bin/Release/net9.0
```

## License

MIT
