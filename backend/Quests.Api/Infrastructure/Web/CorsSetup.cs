namespace Quests.Api.Infrastructure.Web;

/// <summary>
/// Cross-origin access for the Android app (Capacitor WebView origin <c>https://localhost</c>).
/// Origins come from <c>CORS_ORIGINS</c> (comma-separated absolute http(s) origins). Empty or missing
/// means no cross-origin access. Credentials are never allowed: auth is a bearer token, not cookies.
/// </summary>
public static class CorsSetup
{
    private const string CorsOriginsKey = "CORS_ORIGINS";
    private const string LoggerCategory = "Quests.Cors";

    public static IServiceCollection AddQuestsCors(this IServiceCollection services, IConfiguration configuration)
    {
        var origins = CorsOrigins.Parse(configuration[CorsOriginsKey]);
        services.AddSingleton(origins);
        services.AddCors(options => options.AddDefaultPolicy(policy => policy
            .WithOrigins([.. origins.Allowed])
            .AllowAnyHeader()
            .AllowAnyMethod()));
        return services;
    }

    /// <summary>Must run before <c>UseAuthentication</c>, so preflight requests are answered without a token.</summary>
    public static WebApplication UseQuestsCors(this WebApplication app)
    {
        app.UseCors();
        return app;
    }

    /// <summary>Logs the configured origins once the app is built (origins are not secret).</summary>
    public static WebApplication LogQuestsCorsSettings(this WebApplication app)
    {
        var origins = app.Services.GetRequiredService<CorsOrigins>();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(LoggerCategory);

        foreach (var entry in origins.Rejected)
            logger.LogWarning("{CorsOriginsKey} entry {Entry} is not an absolute http(s) origin; skipped", CorsOriginsKey, entry);

        logger.LogInformation(
            "CORS allows {Count} origins: {Origins}", origins.Allowed.Count, string.Join(", ", origins.Allowed));
        return app;
    }
}

internal sealed record CorsOrigins(IReadOnlyList<string> Allowed, IReadOnlyList<string> Rejected)
{
    public static CorsOrigins Parse(string? value)
    {
        var allowed = new List<string>();
        var rejected = new List<string>();

        var entries = (value ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        foreach (var entry in entries)
        {
            var origin = TryNormalize(entry);
            if (origin is null)
            {
                rejected.Add(entry);
                continue;
            }

            if (!allowed.Contains(origin, StringComparer.OrdinalIgnoreCase))
                allowed.Add(origin);
        }

        return new CorsOrigins(allowed, rejected);
    }

    // An origin is scheme + host (+ port) only: no path, query or fragment.
    private static string? TryNormalize(string entry)
    {
        if (!Uri.TryCreate(entry, UriKind.Absolute, out var uri))
            return null;

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return null;

        if (uri.AbsolutePath != "/" || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            return null;

        return uri.GetLeftPart(UriPartial.Authority);
    }
}
