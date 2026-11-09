using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Quests.Api.Infrastructure.Data;

/// <summary>EF Core + SQLite registration and startup migrations.</summary>
public static class DataSetup
{
    private const string ConnectionStringName = "Default";

    public static IServiceCollection AddQuestsData(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = GetConnectionString(configuration);
        services.AddDbContext<AppDbContext>(options => options.UseSqlite(connectionString));
        return services;
    }

    /// <summary>Reads the SQLite connection string; throws a clear error when it is missing.</summary>
    internal static string GetConnectionString(IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName);
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                $"Connection string 'ConnectionStrings:{ConnectionStringName}' is missing. " +
                $"Set it in appsettings or via the 'ConnectionStrings__{ConnectionStringName}' environment variable, " +
                "e.g. 'Data Source=quests.db'.");
        }

        return connectionString;
    }

    /// <summary>Applies pending EF Core migrations. Logs and rethrows on failure so startup stops.</summary>
    public static async Task MigrateQuestsDatabaseAsync(this WebApplication app)
    {
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Quests.Data");

        await using var scope = app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var dataSource = new SqliteConnectionStringBuilder(db.Database.GetConnectionString()).DataSource;
        logger.LogDebug("SQLite data source: {DataSource}", Path.GetFullPath(dataSource));

        try
        {
            var pending = (await db.Database.GetPendingMigrationsAsync()).ToList();
            logger.LogDebug("Pending migrations: {PendingMigrations}", string.Join(", ", pending));

            await db.Database.MigrateAsync();
            logger.LogInformation("Database migrations applied: {AppliedCount}", pending.Count);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Database migration failed for data source {DataSource}", dataSource);
            throw;
        }
    }
}
