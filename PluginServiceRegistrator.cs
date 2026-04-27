// PluginServiceRegistrator.cs
using Jellyfin.Plugin.Watchlist.HomeSection;
using Jellyfin.Plugin.Watchlist.Middleware;
using Jellyfin.Plugin.Watchlist.Services;
using Jellyfin.Plugin.Watchlist.Web;
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
        // Primary injection path: register a callback with IAmParadox27's File
        // Transformation plugin. This is the only mechanism that survives in
        // installations where File Transformation intercepts /web/index.html
        // ahead of any plugin middleware.
        services.AddHostedService<FileTransformationRegistrar>();

        // Fallback for installations without File Transformation.
        services.AddTransient<IStartupFilter, BookmarkMiddlewareStartupFilter>();
        services.AddTransient<BookmarkInjectionMiddleware>();
    }
}
