namespace Quests.Api.Infrastructure.Logging;

/// <summary>Resolved logging settings, kept so they can be logged after startup.</summary>
public sealed record LoggingSettings(LogLevel MinimumLevel, string? InvalidValue);
