using Microsoft.Net.Http.Headers;

namespace Quests.Api.Infrastructure.Web;

/// <summary>
/// Serves the React build from <c>wwwroot</c> with an SPA fallback to <c>index.html</c>.
/// Files are public (served before authentication); unknown <c>/api/*</c> paths never get <c>index.html</c>.
/// A missing <c>wwwroot</c> (local dev) is allowed: the fallback then returns 404.
/// </summary>
public static class StaticWebSetup
{
    private const string IndexFile = "index.html";
    private const string LoggerCategory = "Quests.StaticWeb";

    /// <summary>Default files + static files. Must run before <c>UseAuthentication</c>/<c>UseAuthorization</c>.</summary>
    public static WebApplication UseQuestsStaticWeb(this WebApplication app)
    {
        LogWebRoot(app);

        app.UseDefaultFiles();
        app.UseStaticFiles(CreateFileOptions());
        return app;
    }

    /// <summary>Fallback endpoints. Call after all other endpoint mappings.</summary>
    public static WebApplication MapQuestsStaticWeb(this WebApplication app)
    {
        // Keeps the default (authorized) policy: 401 without a token, 404 ProblemDetails with one.
        app.MapFallback("/api/{**path}", () => Results.NotFound());

        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(LoggerCategory);
        app.MapFallbackToFile(IndexFile, CreateFileOptions())
            .AllowAnonymous()
            .Add(endpoint => endpoint.RequestDelegate = LogFallback(endpoint.RequestDelegate!, logger));
        return app;
    }

    private static StaticFileOptions CreateFileOptions() => new()
    {
        OnPrepareResponse = context =>
        {
            if (string.Equals(context.File.Name, IndexFile, StringComparison.OrdinalIgnoreCase))
                context.Context.Response.Headers[HeaderNames.CacheControl] = "no-cache";
        },
    };

    // MapFallbackToFile rewrites Request.Path to /index.html, so the original path is captured first.
    private static RequestDelegate LogFallback(RequestDelegate inner, ILogger logger) => context =>
    {
        logger.LogDebug("SPA fallback served index.html for {Path}", context.Request.Path);
        return inner(context);
    };

    private static void LogWebRoot(WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(LoggerCategory);
        var hasIndex = app.Environment.WebRootFileProvider.GetFileInfo(IndexFile).Exists;

        logger.LogInformation(
            "Static web root {WebRootPath}, index.html present: {HasIndex}", app.Environment.WebRootPath, hasIndex);

        if (!hasIndex && !app.Environment.IsDevelopment())
            logger.LogWarning("index.html is missing from the web root; the web app will not be served");
    }
}
