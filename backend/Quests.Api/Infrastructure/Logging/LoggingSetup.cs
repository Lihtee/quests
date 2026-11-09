using System.Text;
using System.Text.Json;
using NReco.Logging.File;

namespace Quests.Api.Infrastructure.Logging;

/// <summary>
/// Structured JSON logging. The minimum level comes from the <c>LOG_LEVEL</c> env var.
/// Output goes to the console, or — when <c>LOG_FILE</c> is set — only to that file as JSON lines with size rotation.
/// </summary>
public static class LoggingSetup
{
    private const string LogLevelKey = "LOG_LEVEL";
    private const string LogFileKey = "LOG_FILE";
    private const LogLevel DefaultLevel = LogLevel.Information;
    private const string TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fffZ";
    private const long FileSizeLimitBytes = 10 * 1024 * 1024;
    private const int MaxRollingFiles = 5;

    private static readonly string[] AllowedLevels =
        ["Trace", "Debug", "Information", "Warning", "Error", "Critical"];

    public static WebApplicationBuilder AddQuestsLogging(this WebApplicationBuilder builder)
    {
        var settings = ResolveSettings(builder.Configuration[LogLevelKey], builder.Configuration[LogFileKey]);

        builder.Logging.ClearProviders();
        if (settings.LogFile is null)
            AddConsole(builder.Logging);
        else
            AddFile(builder.Logging, settings.LogFile);
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

        if (settings.LogFile is not null)
            logger.LogInformation("Logging to file {LogFile}", settings.LogFile);

        if (settings.InvalidValue is not null)
        {
            logger.LogWarning(
                "Invalid {LogLevelKey} value {InvalidValue}; using {MinimumLevel}. Allowed: {AllowedLevels}",
                LogLevelKey, settings.InvalidValue, settings.MinimumLevel, string.Join("|", AllowedLevels));
        }

        logger.LogDebug("Effective minimum log level: {MinimumLevel}", settings.MinimumLevel);
        return app;
    }

    private static void AddConsole(ILoggingBuilder logging) =>
        logging.AddJsonConsole(options =>
        {
            options.IncludeScopes = true;
            options.UseUtcTimestamp = true;
            options.TimestampFormat = TimestampFormat;
            options.JsonWriterOptions = new JsonWriterOptions { Indented = false };
        });

    private static void AddFile(ILoggingBuilder logging, string logFile)
    {
        var directory = Path.GetDirectoryName(Path.GetFullPath(logFile));
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        logging.AddFile(logFile, options =>
        {
            options.Append = true;
            options.FileSizeLimitBytes = FileSizeLimitBytes;
            options.MaxRollingFiles = MaxRollingFiles;
            options.UseUtcTimestamp = true;
            options.FormatLogEntry = FormatJsonLine;
        });
    }

    /// <summary>One JSON object per line: Timestamp, LogLevel, Category, EventId, Message, Exception (when present).</summary>
    private static string FormatJsonLine(LogMessage entry)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("Timestamp", DateTime.UtcNow.ToString(TimestampFormat));
            writer.WriteString("LogLevel", entry.LogLevel.ToString());
            writer.WriteString("Category", entry.LogName);
            writer.WriteNumber("EventId", entry.EventId.Id);
            writer.WriteString("Message", entry.Message);
            if (entry.Exception is not null)
                writer.WriteString("Exception", entry.Exception.ToString());
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static LoggingSettings ResolveSettings(string? rawLevel, string? rawLogFile)
    {
        var logFile = string.IsNullOrWhiteSpace(rawLogFile) ? null : rawLogFile.Trim();

        if (string.IsNullOrWhiteSpace(rawLevel))
            return new LoggingSettings(DefaultLevel, InvalidValue: null, logFile);

        var name = Array.Find(AllowedLevels, l => l.Equals(rawLevel.Trim(), StringComparison.OrdinalIgnoreCase));
        if (name is null)
            return new LoggingSettings(DefaultLevel, InvalidValue: rawLevel, logFile);

        return new LoggingSettings(Enum.Parse<LogLevel>(name), InvalidValue: null, logFile);
    }
}
