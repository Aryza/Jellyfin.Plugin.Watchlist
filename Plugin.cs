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
