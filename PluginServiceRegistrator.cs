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
