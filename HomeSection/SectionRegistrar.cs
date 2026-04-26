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
        var ct = AssemblyLoadContext.All
            .SelectMany(c => c.Assemblies)
            .FirstOrDefault(a => a.FullName?.Contains(".CustomTabs", StringComparison.Ordinal) == true);

        if (ct is null)
        {
            _logger.LogInformation("Watchlist: CustomTabs plugin not installed — Watchlist tab skipped.");
            return;
        }

        var pi     = ct.GetType("Jellyfin.Plugin.CustomTabs.PluginInterface");
        var method = pi?.GetMethods(BindingFlags.Static | BindingFlags.Public)
                         .FirstOrDefault(m => m.Name.StartsWith("Register", StringComparison.OrdinalIgnoreCase));
        if (method is null)
        {
            _logger.LogWarning("Watchlist: CustomTabs PluginInterface register method not found. " +
                "Inspect the assembly to find the correct method name and payload format.");
            return;
        }

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
