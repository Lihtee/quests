using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;

namespace Quests.Tests;

/// <summary>Hosts the API in-memory with a unique temp SQLite file that is deleted on dispose.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"quests-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={_databasePath}");
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();

        // Pooled SQLite connections keep the file open on Windows.
        SqliteConnection.ClearAllPools();
        if (File.Exists(_databasePath))
            File.Delete(_databasePath);
    }
}
