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
