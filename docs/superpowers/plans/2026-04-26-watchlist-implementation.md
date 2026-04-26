# Watchlist Plugin Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Standalone Jellyfin plugin that provides a per-user watchlist: bookmark button injected into item detail pages, home-screen row via HomeScreenSections, and a full card-grid tab via CustomTabs.

**Architecture:** Middleware injects a JS bookmark button (next to `.btnFavorite`) into Jellyfin's SPA. Watchlist data stored as JSON in Jellyfin's DataPath. A `WatchlistService` singleton manages reads/writes under a lock. An `IEventConsumer<UserDataSaveEventArgs>` auto-removes items when marked played. Home row and custom tab use IAmParadox27's plugins via reflection, same pattern as MissingMediaChecker.

**Tech Stack:** .NET 9, Jellyfin SDK 10.11, ASP.NET Core middleware, Newtonsoft.Json, IAmParadox27 HomeScreenSections + CustomTabs plugins (optional runtime deps).

---

## File Map

| File | Responsibility |
|------|---------------|
| `Jellyfin.Plugin.Watchlist.csproj` | Build config, package refs |
| `build.yaml` | Jellyfin plugin manifest source |
| `manifest.json` | Plugin catalog entry |
| `Plugin.cs` | `BasePlugin<PluginConfiguration>`, `IHasWebPages`, exposes tab HTML page |
| `PluginServiceRegistrator.cs` | `IPluginServiceRegistrator` — wires DI, middleware, hosted services |
| `Configuration/PluginConfiguration.cs` | `HomeRowMaxItems` setting |
| `Configuration/configPage.html` | Plugin settings UI (embedded resource) |
| `Configuration/watchlistTabPage.html` | Watchlist tab card-grid UI (embedded resource) |
| `Models/WatchlistEntry.cs` | `{ JellyfinItemId, MediaType, AddedAt }` |
| `Models/WatchlistStore.cs` | `{ Dictionary<string userId, List<WatchlistEntry>> Entries }` |
| `Services/WatchlistService.cs` | Add/Remove/Get/Contains per user; JSON persistence |
| `Services/WatchlistEventConsumer.cs` | `IEventConsumer<UserDataSaveEventArgs>` — auto-remove on played |
| `Api/WatchlistController.cs` | REST: GET/POST/DELETE Items, GET Status |
| `HomeSection/SectionPayload.cs` | Mirror of HSS payload DTO (no compile-time ref needed) |
| `HomeSection/WatchlistSectionHandler.cs` | Returns last N watchlist items as `QueryResult<BaseItemDto>` |
| `HomeSection/SectionRegistrar.cs` | `IHostedService` — registers home row + tab via reflection |
| `Middleware/BookmarkInjectionMiddleware.cs` | Injects bookmark JS into Jellyfin's `index.html` |

---

## Task 1: Project Scaffold

**Files:**
- Create: `Jellyfin.Plugin.Watchlist.csproj`
- Create: `build.yaml`
- Create: `manifest.json`

- [ ] **Step 1: Create the .csproj**

```xml
<!-- Jellyfin.Plugin.Watchlist.csproj -->
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <AssemblyName>Jellyfin.Plugin.Watchlist</AssemblyName>
    <RootNamespace>Jellyfin.Plugin.Watchlist</RootNamespace>
    <GenerateDocumentationFile>true</GenerateDocumentationFile>
    <TreatWarningsAsErrors>false</TreatWarningsAsErrors>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <Version>1.0.0.0</Version>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Jellyfin.Controller" Version="10.11.*" />
    <PackageReference Include="Jellyfin.Model" Version="10.11.*" />
    <PackageReference Include="Newtonsoft.Json" Version="13.0.1" />
  </ItemGroup>

  <ItemGroup>
    <EmbeddedResource Include="Configuration\configPage.html" />
    <EmbeddedResource Include="Configuration\watchlistTabPage.html" />
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Create build.yaml**

```yaml
---
name: "Watchlist"
guid: "a1b2c3d4-e5f6-7890-abcd-ef1234567890"
version: "1.0.0.0"
targetAbi: "10.11.0.0"
framework: "net9.0"
owner: "Ary"
category: "General"
overview: "Per-user watchlist with home-screen row and library tab."
description: >-
  Bookmark any movie or series from its detail page. Bookmarked items appear
  in a home-screen row (requires HomeScreenSections plugin) and a dedicated
  Watchlist tab (requires CustomTabs plugin). Items auto-remove when marked
  as played.
artifacts:
  - "Jellyfin.Plugin.Watchlist.dll"
changelog: |-
  Initial release.
```

- [ ] **Step 3: Create manifest.json**

```json
[
  {
    "guid": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    "name": "Watchlist",
    "description": "Bookmark any movie or series from its detail page. Bookmarked items appear in a home-screen row and a dedicated Watchlist tab. Items auto-remove when marked as played.",
    "overview": "Per-user watchlist with home-screen row and library tab.",
    "owner": "Ary",
    "category": "General",
    "imageUrl": "",
    "versions": []
  }
]
```

- [ ] **Step 4: Init git and verify build skeleton compiles (no source files yet)**

```bash
cd /Users/ary/Documents/coding/Jellyfin.Plugin.Watchlist
git branch -m master main
dotnet restore Jellyfin.Plugin.Watchlist.csproj
```

Expected: `Restore complete.`

- [ ] **Step 5: Commit**

```bash
git add Jellyfin.Plugin.Watchlist.csproj build.yaml manifest.json
git commit -m "chore: scaffold project"
```

---

## Task 2: Data Models

**Files:**
- Create: `Models/WatchlistEntry.cs`
- Create: `Models/WatchlistStore.cs`

- [ ] **Step 1: Create WatchlistEntry**

```csharp
// Models/WatchlistEntry.cs
namespace Jellyfin.Plugin.Watchlist.Models;

public class WatchlistEntry
{
    public Guid            JellyfinItemId { get; set; }
    public string          MediaType      { get; set; } = string.Empty; // "Movie" | "Series"
    public DateTimeOffset  AddedAt        { get; set; }
}
```

- [ ] **Step 2: Create WatchlistStore**

```csharp
// Models/WatchlistStore.cs
namespace Jellyfin.Plugin.Watchlist.Models;

public class WatchlistStore
{
    /// <summary>userId (Guid.ToString("N")) → ordered list of watchlist entries.</summary>
    public Dictionary<string, List<WatchlistEntry>> Entries { get; set; } = new();
}
```

- [ ] **Step 3: Confirm project builds**

```bash
dotnet build Jellyfin.Plugin.Watchlist.csproj -c Release 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add Models/
git commit -m "feat: data models WatchlistEntry + WatchlistStore"
```

---

## Task 3: WatchlistService

**Files:**
- Create: `Services/WatchlistService.cs`

- [ ] **Step 1: Create WatchlistService**

```csharp
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

    /// <summary>Removes item. No-ops if not present.</summary>
    public void Remove(Guid userId, Guid itemId)
    {
        lock (_lock)
        {
            var store = Load();
            var key   = Key(userId);
            if (!store.Entries.TryGetValue(key, out var list)) return;

            var before = list.Count;
            list.RemoveAll(e => e.JellyfinItemId == itemId);
            if (list.Count != before) Save(store);
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
            var json = JsonSerializer.Serialize(store, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_storePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Watchlist: failed to save store to {Path}", _storePath);
        }
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build Jellyfin.Plugin.Watchlist.csproj -c Release 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add Services/WatchlistService.cs
git commit -m "feat: WatchlistService - add/remove/get/contains per user"
```

---

## Task 4: API Controller

**Files:**
- Create: `Api/WatchlistController.cs`

- [ ] **Step 1: Create WatchlistController**

```csharp
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

        var mediaType = item.GetType().Name; // "Movie", "Series", etc.
        _watchlist.Add(userId, itemId, mediaType);
        return Ok(new { inWatchlist = true });
    }

    // ── DELETE /Watchlist/Items/{itemId} ─────────────────────────────────────
    [HttpDelete("Items/{itemId}")]
    public ActionResult RemoveItem(Guid itemId)
    {
        var userId = GetUserId();
        if (userId == Guid.Empty) return Unauthorized();

        if (!_watchlist.Contains(userId, itemId))
            return NotFound(new { message = "Not in watchlist." });

        _watchlist.Remove(userId, itemId);
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
```

- [ ] **Step 2: Build**

```bash
dotnet build Jellyfin.Plugin.Watchlist.csproj -c Release 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add Api/WatchlistController.cs
git commit -m "feat: WatchlistController REST endpoints"
```

---

## Task 5: Event Consumer (Auto-remove on Watched)

**Files:**
- Create: `Services/WatchlistEventConsumer.cs`

- [ ] **Step 1: Create WatchlistEventConsumer**

```csharp
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

        if (_watchlist.Contains(userId, itemId))
        {
            _watchlist.Remove(userId, itemId);
            _logger.LogInformation(
                "Watchlist: auto-removed item {ItemId} for user {UserId} (marked played)",
                itemId, userId);
        }

        return Task.CompletedTask;
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build Jellyfin.Plugin.Watchlist.csproj -c Release 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add Services/WatchlistEventConsumer.cs
git commit -m "feat: WatchlistEventConsumer - auto-remove on played"
```

---

## Task 6: Home Screen Section

**Files:**
- Create: `HomeSection/SectionPayload.cs`
- Create: `HomeSection/WatchlistSectionHandler.cs`
- Create: `HomeSection/SectionRegistrar.cs`

- [ ] **Step 1: Create SectionPayload**

```csharp
// HomeSection/SectionPayload.cs
namespace Jellyfin.Plugin.Watchlist.HomeSection;

/// <summary>
/// Mirrors Jellyfin.Plugin.HomeScreenSections.Model.Dto.HomeScreenSectionPayload.
/// HSS deserialises into whatever type the handler method signature declares —
/// no compile-time reference to the HSS assembly is needed.
/// </summary>
public sealed class SectionPayload
{
    public Guid    UserId         { get; set; }
    public string? AdditionalData { get; set; }
}
```

- [ ] **Step 2: Create WatchlistSectionHandler**

```csharp
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
```

- [ ] **Step 3: Create SectionRegistrar**

```csharp
// HomeSection/SectionRegistrar.cs
using System.Reflection;
using System.Runtime.Loader;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace Jellyfin.Plugin.Watchlist.HomeSection;

/// <summary>
/// Registers the Watchlist home-screen row with IAmParadox27's HSS plugin,
/// and the Watchlist tab with IAmParadox27's CustomTabs plugin — both via
/// reflection at runtime so neither is a compile-time dependency.
/// Runs as an IHostedService so all plugins are loaded before registration.
/// </summary>
public sealed class SectionRegistrar : IHostedService
{
    private readonly ILogger<SectionRegistrar> _logger;

    public SectionRegistrar(ILogger<SectionRegistrar> logger) => _logger = logger;

    public Task StartAsync(CancellationToken ct)
    {
        try { RegisterHomeSection(); } catch (Exception ex) { _logger.LogWarning(ex, "Watchlist: HSS registration failed"); }
        try { RegisterCustomTab(); }   catch (Exception ex) { _logger.LogWarning(ex, "Watchlist: CustomTabs registration failed"); }
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken ct) => Task.CompletedTask;

    private void RegisterHomeSection()
    {
        var hss = AssemblyLoadContext.All
            .SelectMany(c => c.Assemblies)
            .FirstOrDefault(a => a.FullName?.Contains(".HomeScreenSections", StringComparison.Ordinal) == true);

        if (hss is null)
        {
            _logger.LogInformation("Watchlist: HomeScreenSections plugin not installed — home row skipped.");
            return;
        }

        var pi     = hss.GetType("Jellyfin.Plugin.HomeScreenSections.PluginInterface");
        var method = pi?.GetMethod("RegisterSection");
        if (method is null)
        {
            _logger.LogWarning("Watchlist: HSS PluginInterface.RegisterSection not found.");
            return;
        }

        var payload = new JObject
        {
            ["id"]              = "watchlist-row",
            ["displayText"]     = "My Watchlist",
            ["limit"]           = 1,
            ["route"]           = null,
            ["additionalData"]  = null,
            ["resultsAssembly"] = GetType().Assembly.FullName,
            ["resultsClass"]    = typeof(WatchlistSectionHandler).FullName!,
            ["resultsMethod"]   = "GetResults"
        };
        method.Invoke(null, new object?[] { payload });
        _logger.LogInformation("Watchlist: registered HSS home row.");
    }

    private void RegisterCustomTab()
    {
        // IAmParadox27's CustomTabs plugin — probe for its PluginInterface.
        // The tab page is served by our plugin via IHasWebPages at the path below.
        var ct = AssemblyLoadContext.All
            .SelectMany(c => c.Assemblies)
            .FirstOrDefault(a => a.FullName?.Contains(".CustomTabs", StringComparison.Ordinal) == true);

        if (ct is null)
        {
            _logger.LogInformation("Watchlist: CustomTabs plugin not installed — Watchlist tab skipped.");
            return;
        }

        // Probe for RegisterTab — inspect the assembly at runtime if this fails.
        var pi     = ct.GetType("Jellyfin.Plugin.CustomTabs.PluginInterface");
        var method = pi?.GetMethods(BindingFlags.Static | BindingFlags.Public)
                         .FirstOrDefault(m => m.Name.StartsWith("Register", StringComparison.OrdinalIgnoreCase));
        if (method is null)
        {
            _logger.LogWarning("Watchlist: CustomTabs PluginInterface register method not found. " +
                "Inspect the assembly to find the correct method name and payload format.");
            return;
        }

        // Standard CustomTabs payload (adjust field names to match the plugin's actual API).
        var payload = new JObject
        {
            ["id"]          = "watchlist-tab",
            ["displayText"] = "Watchlist",
            ["url"]         = "/web/configpages/Watchlist.html"
        };
        method.Invoke(null, new object?[] { payload });
        _logger.LogInformation("Watchlist: registered CustomTabs tab.");
    }
}
```

- [ ] **Step 4: Build**

```bash
dotnet build Jellyfin.Plugin.Watchlist.csproj -c Release 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 5: Commit**

```bash
git add HomeSection/
git commit -m "feat: home section handler + registrar"
```

---

## Task 7: Bookmark Injection Middleware

**Files:**
- Create: `Middleware/BookmarkInjectionMiddleware.cs`

- [ ] **Step 1: Create BookmarkInjectionMiddleware**

```csharp
// Middleware/BookmarkInjectionMiddleware.cs
using System.IO.Compression;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Watchlist.Middleware;

/// <summary>
/// Intercepts GET /web/index.html and injects the watchlist bookmark script.
/// Handles gzip/deflate/br compressed responses (e.g. from FileTransformation plugin).
/// </summary>
public sealed class BookmarkInjectionMiddleware
{
    private const string ScriptTag = """
        <script>
        (function () {
            'use strict';

            var STORAGE_KEY = 'wl_token';

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

                // Skip non-detail pages
                var hash = window.location.hash || '';
                if (!hash.includes('details') && !hash.includes('item')) return;

                // Wait for favourite button to appear
                var favBtn = document.querySelector('.btnFavorite');
                if (!favBtn || document.querySelector('.btnWatchlistToggle')) return;

                // Check current status
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

                    favBtn.parentNode.insertBefore(btn, favBtn.nextSibling);
                } catch (e) {
                    console.warn('Watchlist inject error', e);
                }
            }

            // Re-inject on SPA navigation
            var _lastHash = '';
            var observer  = new MutationObserver(function () {
                var h = window.location.hash;
                if (h !== _lastHash) { _lastHash = h; setTimeout(injectButton, 400); }
            });
            document.addEventListener('DOMContentLoaded', function () {
                observer.observe(document.body, { childList: true, subtree: true });
            });
        })();
        </script>
        """;

    private readonly RequestDelegate                     _next;
    private readonly ILogger<BookmarkInjectionMiddleware> _logger;

    public BookmarkInjectionMiddleware(RequestDelegate next, ILogger<BookmarkInjectionMiddleware> logger)
    {
        _next   = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var path = context.Request.Path.Value ?? string.Empty;

        // Only intercept index.html GETs
        if (!context.Request.Method.Equals("GET", StringComparison.OrdinalIgnoreCase)
            || !path.EndsWith("index.html", StringComparison.OrdinalIgnoreCase))
        {
            await _next(context);
            return;
        }

        // Buffer the downstream response
        var originalBody = context.Response.Body;
        using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        await _next(context);

        buffer.Seek(0, SeekOrigin.Begin);
        var encoding = context.Response.Headers.ContentEncoding.ToString();
        string html;

        try
        {
            html = await DecompressAsync(buffer, encoding);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Watchlist: failed to decompress index.html ({Encoding}); skipping injection", encoding);
            buffer.Seek(0, SeekOrigin.Begin);
            context.Response.Body = originalBody;
            await buffer.CopyToAsync(originalBody);
            return;
        }

        if (html.Contains("</body>", StringComparison.OrdinalIgnoreCase))
        {
            html = html.Replace("</body>", ScriptTag + "</body>", StringComparison.OrdinalIgnoreCase);
            context.Response.Headers.Remove("Content-Encoding");
            context.Response.Headers.Remove("Content-Length");
            var bytes = Encoding.UTF8.GetBytes(html);
            context.Response.Body = originalBody;
            context.Response.ContentLength = bytes.Length;
            await originalBody.WriteAsync(bytes);
        }
        else
        {
            buffer.Seek(0, SeekOrigin.Begin);
            context.Response.Body = originalBody;
            await buffer.CopyToAsync(originalBody);
        }
    }

    private static async Task<string> DecompressAsync(Stream stream, string encoding)
    {
        if (string.IsNullOrEmpty(encoding) || encoding.Equals("identity", StringComparison.OrdinalIgnoreCase))
        {
            using var reader = new StreamReader(stream, Encoding.UTF8, leaveOpen: true);
            return await reader.ReadToEndAsync();
        }

        Stream decompressed = encoding.ToLowerInvariant() switch
        {
            "gzip"    => new GZipStream(stream, CompressionMode.Decompress, leaveOpen: true),
            "deflate" => new DeflateStream(stream, CompressionMode.Decompress, leaveOpen: true),
            "br"      => new BrotliStream(stream, CompressionMode.Decompress, leaveOpen: true),
            _         => stream
        };

        using var r = new StreamReader(decompressed, Encoding.UTF8);
        return await r.ReadToEndAsync();
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build Jellyfin.Plugin.Watchlist.csproj -c Release 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add Middleware/BookmarkInjectionMiddleware.cs
git commit -m "feat: bookmark injection middleware"
```

---

## Task 8: Plugin Entry Point + Configuration + DI Wiring

**Files:**
- Create: `Configuration/PluginConfiguration.cs`
- Create: `Plugin.cs`
- Create: `PluginServiceRegistrator.cs`

- [ ] **Step 1: Create PluginConfiguration**

```csharp
// Configuration/PluginConfiguration.cs
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Watchlist.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Maximum items shown in the home-screen row.</summary>
    public int HomeRowMaxItems { get; set; } = 20;
}
```

- [ ] **Step 2: Create Plugin.cs**

```csharp
// Plugin.cs
using Jellyfin.Plugin.Watchlist.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Watchlist;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name        => "Watchlist";
    public override Guid   Id          => Guid.Parse("a1b2c3d4-e5f6-7890-abcd-ef1234567890");
    public override string Description => "Per-user watchlist with home-screen row and library tab.";

    public IEnumerable<PluginPageInfo> GetPages() => new[]
    {
        // Settings page (accessible from Dashboard → Plugins)
        new PluginPageInfo
        {
            Name                 = Name,
            EnableInMainMenu     = true,
            EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html"
        },
        // Watchlist tab page (URL: /web/configpages/Watchlist.html)
        new PluginPageInfo
        {
            Name                 = "Watchlist",
            EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.watchlistTabPage.html"
        }
    };
}
```

- [ ] **Step 3: Create PluginServiceRegistrator**

```csharp
// PluginServiceRegistrator.cs
using Jellyfin.Plugin.Watchlist.HomeSection;
using Jellyfin.Plugin.Watchlist.Middleware;
using Jellyfin.Plugin.Watchlist.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Watchlist;

public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection services, IServerApplicationHost applicationHost)
    {
        // Core service — singleton so data is consistent across DI scopes
        services.AddSingleton<WatchlistService>();

        // Home section handler — transient (instantiated per HSS call via reflection)
        services.AddTransient<WatchlistSectionHandler>();

        // Hosted service: registers home row + tab with optional plugins at startup
        services.AddHostedService<SectionRegistrar>();

        // Auto-remove on watched
        services.AddScoped<IEventConsumer<UserDataSaveEventArgs>, WatchlistEventConsumer>();
    }
}
```

- [ ] **Step 4: Register middleware in application pipeline**

Jellyfin doesn't expose a `Configure(IApplicationBuilder)` hook in `IPluginServiceRegistrator`. Instead, add middleware registration via `IStartupFilter`. Add to `PluginServiceRegistrator.RegisterServices`:

```csharp
// Add inside RegisterServices, after AddHostedService<SectionRegistrar>():
services.AddTransient<IStartupFilter, BookmarkMiddlewareStartupFilter>();
```

Then create the startup filter class in `Middleware/BookmarkMiddlewareStartupFilter.cs`:

```csharp
// Middleware/BookmarkMiddlewareStartupFilter.cs
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Jellyfin.Plugin.Watchlist.Middleware;

public sealed class BookmarkMiddlewareStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        => app =>
        {
            app.UseMiddleware<BookmarkInjectionMiddleware>();
            next(app);
        };
}
```

Update `PluginServiceRegistrator.cs` to add the startup filter registration after the hosted service:

```csharp
services.AddTransient<IStartupFilter, BookmarkMiddlewareStartupFilter>();
```

Full updated `PluginServiceRegistrator.cs`:

```csharp
// PluginServiceRegistrator.cs
using Jellyfin.Plugin.Watchlist.HomeSection;
using Jellyfin.Plugin.Watchlist.Middleware;
using Jellyfin.Plugin.Watchlist.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Events;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Watchlist;

public sealed class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection services, IServerApplicationHost applicationHost)
    {
        services.AddSingleton<WatchlistService>();
        services.AddTransient<WatchlistSectionHandler>();
        services.AddHostedService<SectionRegistrar>();
        services.AddScoped<IEventConsumer<UserDataSaveEventArgs>, WatchlistEventConsumer>();
        services.AddTransient<IStartupFilter, BookmarkMiddlewareStartupFilter>();
    }
}
```

- [ ] **Step 5: Build**

```bash
dotnet build Jellyfin.Plugin.Watchlist.csproj -c Release 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 6: Commit**

```bash
git add Configuration/PluginConfiguration.cs Plugin.cs PluginServiceRegistrator.cs Middleware/BookmarkMiddlewareStartupFilter.cs
git commit -m "feat: Plugin entry point, configuration, DI wiring"
```

---

## Task 9: HTML Pages

**Files:**
- Create: `Configuration/configPage.html` (settings)
- Create: `Configuration/watchlistTabPage.html` (watchlist tab)

- [ ] **Step 1: Create configPage.html (settings UI)**

```html
<!DOCTYPE html>
<html>
<head>
    <title>Watchlist</title>
</head>
<body>
<div id="WatchlistConfigPage" data-role="page" class="page type-interior pluginConfigurationPage" data-require="emby-input,emby-button,emby-select">
    <div data-role="content">
        <div class="content-primary">
            <form id="WatchlistConfigForm">
                <div class="sectionTitleContainer flex align-items-center">
                    <h2 class="sectionTitle">Watchlist Settings</h2>
                </div>
                <div class="inputContainer">
                    <label class="inputLabel inputLabelUnfocused" for="homeRowMaxItems">Home screen row — max items</label>
                    <input type="number" id="homeRowMaxItems" class="emby-input" min="1" max="100" value="20" />
                    <div class="fieldDescription">Maximum number of watchlist items shown in the home-screen row.</div>
                </div>
                <div>
                    <button is="emby-button" type="submit" class="raised button-submit block emby-button">
                        <span>Save</span>
                    </button>
                </div>
            </form>
        </div>
    </div>
    <script type="text/javascript">
        var WatchlistConfig = {
            pluginUniqueId: 'a1b2c3d4-e5f6-7890-abcd-ef1234567890',

            loadConfig: function (page) {
                ApiClient.getPluginConfiguration(WatchlistConfig.pluginUniqueId).then(function (config) {
                    page.querySelector('#homeRowMaxItems').value = config.HomeRowMaxItems || 20;
                });
            },

            saveConfig: function (page) {
                ApiClient.getPluginConfiguration(WatchlistConfig.pluginUniqueId).then(function (config) {
                    config.HomeRowMaxItems = parseInt(page.querySelector('#homeRowMaxItems').value, 10) || 20;
                    ApiClient.updatePluginConfiguration(WatchlistConfig.pluginUniqueId, config).then(function () {
                        Dashboard.processPluginConfigurationUpdateResult();
                    });
                });
            }
        };

        document.querySelector('#WatchlistConfigPage').addEventListener('pageshow', function () {
            WatchlistConfig.loadConfig(this);
        });

        document.querySelector('#WatchlistConfigForm').addEventListener('submit', function (e) {
            e.preventDefault();
            WatchlistConfig.saveConfig(document.querySelector('#WatchlistConfigPage'));
        });
    </script>
</div>
</body>
</html>
```

- [ ] **Step 2: Create watchlistTabPage.html (card-grid view)**

```html
<!DOCTYPE html>
<html>
<head>
    <title>Watchlist</title>
    <style>
        #watchlistTabPage { padding: 1.5em 2em; }
        #watchlistTabPage h2 { margin: 0 0 1em; font-size: 1.4em; font-weight: 600; }
        #wlEmpty { display: none; text-align: center; padding: 4em 1em; opacity: 0.55; font-size: 1em; }
        #wlEmpty.visible { display: block; }
        #wlLoading { text-align: center; padding: 4em 1em; opacity: 0.55; }
        #wlGrid { display: flex; flex-wrap: wrap; gap: 1em; }
    </style>
</head>
<body>
<div id="watchlistTabPage" data-role="page">
    <div data-role="content">
        <div class="content-primary">
            <div class="sectionTitleContainer flex align-items-center" style="margin-bottom:1em;">
                <h2 class="sectionTitle">My Watchlist</h2>
            </div>
            <div id="wlLoading">Loading…</div>
            <div id="wlEmpty">
                <p>Your watchlist is empty.</p>
                <p>Tap the <strong>🔖</strong> icon on any movie or series to add it here.</p>
            </div>
            <div id="wlGrid" class="itemsContainer vertical-wrap"></div>
        </div>
    </div>

    <script type="text/javascript">
    (function () {
        var page = document.getElementById('watchlistTabPage');

        async function loadWatchlist() {
            var loading = document.getElementById('wlLoading');
            var empty   = document.getElementById('wlEmpty');
            var grid    = document.getElementById('wlGrid');

            loading.style.display = '';
            empty.classList.remove('visible');
            grid.innerHTML = '';

            try {
                var token = window.ApiClient ? window.ApiClient.accessToken() : '';
                var resp  = await fetch('/Watchlist/Items', {
                    headers: { 'Authorization': 'MediaBrowser Token=' + token }
                });
                if (!resp.ok) throw new Error('HTTP ' + resp.status);
                var entries = await resp.json();

                loading.style.display = 'none';

                if (!entries || entries.length === 0) {
                    empty.classList.add('visible');
                    return;
                }

                var ids     = entries.map(function (e) { return e.jellyfinItemId; }).join(',');
                var userId  = window.ApiClient.getCurrentUserId();
                var items   = await window.ApiClient.getItems(userId, {
                    Ids:              ids,
                    Fields:           'PrimaryImageAspectRatio,Overview',
                    ImageTypeLimit:   1,
                    EnableImageTypes: 'Primary,Thumb,Backdrop'
                });

                if (!items || !items.Items || items.Items.length === 0) {
                    empty.classList.add('visible');
                    return;
                }

                // Use Jellyfin's native cardBuilder for identical look to movies view
                require(['cardBuilder', 'apphost'], function (cardBuilder, appHost) {
                    cardBuilder.buildCards(items.Items, {
                        itemsContainer:   grid,
                        shape:            'portrait',
                        showTitle:        true,
                        showYear:         true,
                        overlayPlayButton: true,
                        overlayMoreButton: true,
                        centerText:       false,
                        cardLayout:       false,
                        serverId:         window.ApiClient.serverId()
                    });
                });
            } catch (e) {
                loading.style.display = 'none';
                grid.innerHTML = '<p style="color:#f87171;">Failed to load watchlist: ' + e.message + '</p>';
            }
        }

        page.addEventListener('pageshow', loadWatchlist);
    })();
    </script>
</div>
</body>
</html>
```

- [ ] **Step 3: Build (verifies embedded resources compile)**

```bash
dotnet build Jellyfin.Plugin.Watchlist.csproj -c Release 2>&1 | tail -5
```

Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add Configuration/
git commit -m "feat: settings page + watchlist tab HTML"
```

---

## Task 10: Build, Release, and Manifest

**Files:**
- Modify: `manifest.json`

- [ ] **Step 1: Publish**

```bash
dotnet publish Jellyfin.Plugin.Watchlist.csproj -c Release -o bin/Release/net9.0 2>&1 | tail -5
```

Expected: `Jellyfin.Plugin.Watchlist -> bin/Release/net9.0/`

- [ ] **Step 2: Create zip**

```bash
cd bin/Release/net9.0 && zip watchlist_1.0.0.0.zip Jellyfin.Plugin.Watchlist.dll && cd ../../..
```

- [ ] **Step 3: Create GitHub repo and release**

First create repo on GitHub (via browser or `gh repo create`):

```bash
gh repo create Aryza/Jellyfin.Plugin.Watchlist --public --source=. --remote=origin --push
gh release create v1.0.0.0 \
    bin/Release/net9.0/watchlist_1.0.0.0.zip \
    --repo Aryza/Jellyfin.Plugin.Watchlist \
    --title "v1.0.0.0" \
    --notes "Initial release. Per-user watchlist with bookmark button injection, home-screen row, and Watchlist tab."
```

- [ ] **Step 4: Get checksum from GH artifact (wait ~60s after upload)**

```bash
sleep 60
curl -sL "https://github.com/Aryza/Jellyfin.Plugin.Watchlist/releases/download/v1.0.0.0/watchlist_1.0.0.0.zip" \
    -o /tmp/check_wl_1.0.0.0.zip
md5 /tmp/check_wl_1.0.0.0.zip
```

Download a second time to confirm the checksum is stable (GitHub can serve different bytes while still processing):

```bash
sleep 30
curl -sL "https://github.com/Aryza/Jellyfin.Plugin.Watchlist/releases/download/v1.0.0.0/watchlist_1.0.0.0.zip" \
    -o /tmp/check_wl_1.0.0.0_b.zip
md5 /tmp/check_wl_1.0.0.0_b.zip
```

Both checksums must match before updating the manifest.

- [ ] **Step 5: Update manifest.json with real checksum**

Replace `CHECKSUM_HERE` with the md5 from step 4:

```json
[
  {
    "guid": "a1b2c3d4-e5f6-7890-abcd-ef1234567890",
    "name": "Watchlist",
    "description": "Bookmark any movie or series from its detail page. Bookmarked items appear in a home-screen row and a dedicated Watchlist tab. Items auto-remove when marked as played.",
    "overview": "Per-user watchlist with home-screen row and library tab.",
    "owner": "Ary",
    "category": "General",
    "imageUrl": "",
    "versions": [
      {
        "version": "1.0.0.0",
        "changelog": "Initial release.",
        "targetAbi": "10.11.0.0",
        "sourceUrl": "https://github.com/Aryza/Jellyfin.Plugin.Watchlist/releases/download/v1.0.0.0/watchlist_1.0.0.0.zip",
        "checksum": "CHECKSUM_HERE",
        "timestamp": "2026-04-26T00:00:00Z"
      }
    ]
  }
]
```

- [ ] **Step 6: Commit and push**

```bash
git add manifest.json
git commit -m "chore: update manifest for v1.0.0.0"
git push origin main
```

---

## Notes for CustomTabs Integration

If CustomTabs plugin registration fails (method name mismatch), inspect the loaded assembly at runtime by adding a temporary log line to `SectionRegistrar.RegisterCustomTab`:

```csharp
// Add before the method probe to log all available static methods:
if (pi is not null)
{
    var methods = pi.GetMethods(BindingFlags.Static | BindingFlags.Public);
    foreach (var m in methods)
        _logger.LogInformation("Watchlist: CustomTabs method: {Name}({Params})",
            m.Name, string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name)));
}
```

Check Jellyfin logs for the method name and parameter types, then update the `RegisterCustomTab` method accordingly.
