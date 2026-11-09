using System.Text.Json;

namespace Quests.Api.Infrastructure.Logging;

/// <summary>
/// Structured JSON console logging. The minimum level comes from the <c>LOG_LEVEL</c> env var.
/// </summary>
public static class LoggingSetup
{
    private const string LogLevelKey = "LOG_LEVEL";
    private const LogLevel DefaultLevel = LogLevel.Information;

    private static readonly string[] AllowedLevels =
        ["Trace", "Debug", "Information", "Warning", "Error", "Critical"];

    public static WebApplicationBuilder AddQuestsLogging(this WebApplicationBuilder builder)
    {
        var rawLevel = builder.Configuration[LogLevelKey];
        var settings = ResolveSettings(rawLevel);

        builder.Logging.ClearProviders();
        builder.Logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.UseUtcTimestamp = true;
            options.TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
            options.JsonWriterOptions = new JsonWriterOptions { Indented = false };
        });
        builder.Logging.SetMinimumLevel(settings.MinimumLevel);

        builder.Services.AddSingleton(settings);
        return builder;
    }

    /// <summary>
    /// Logs the effective logging settings once the app is built (and warns about an invalid <c>LOG_LEVEL</c>).
    /// </summary>
    public static WebApplication LogQuestsLoggingSettings(this WebApplication app)
    {
        var settings = app.Services.GetRequiredService<LoggingSettings>();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Quests.Logging");

        if (settings.InvalidValue is not null)
        {
            logger.LogWarning(
                "Invalid {LogLevelKey} value {InvalidValue}; using {MinimumLevel}. Allowed: {AllowedLevels}",
                LogLevelKey, settings.InvalidValue, settings.MinimumLevel, string.Join("|", AllowedLevels));
        }

        logger.LogDebug("Effective minimum log level: {MinimumLevel}", settings.MinimumLevel);
        return app;
    }

    private static LoggingSettings ResolveSettings(string? rawLevel)
    {
        if (string.IsNullOrWhiteSpace(rawLevel))
            return new LoggingSettings(DefaultLevel, InvalidValue: null);

        var name = Array.Find(AllowedLevels, l => l.Equals(rawLevel.Trim(), StringComparison.OrdinalIgnoreCase));
        if (name is null)
            return new LoggingSettings(DefaultLevel, InvalidValue: rawLevel);

        return new LoggingSettings(Enum.Parse<LogLevel>(name), InvalidValue: null);
    }
}
