// Configuration/PluginConfiguration.cs
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Watchlist.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>Maximum items shown in the home-screen row.</summary>
    public int HomeRowMaxItems { get; set; } = 20;
}
