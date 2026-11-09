namespace Quests.Api.Infrastructure.Logging;

/// <summary>Resolved logging settings, kept so they can be logged after startup.</summary>
/// <param name="LogFile">Target file when <c>LOG_FILE</c> is set (console logging is then off); otherwise null.</param>
public sealed record LoggingSettings(LogLevel MinimumLevel, string? InvalidValue, string? LogFile = null);
