using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Quests.Api.Features.Identity;

namespace Quests.Api.Infrastructure.Data;

/// <summary>
/// Used only by <c>dotnet ef</c>: builds <see cref="AppDbContext"/> without running <c>Program.cs</c>,
/// so design-time tooling does not depend on runtime settings such as auth configuration.
/// Reads the connection string like the app does (appsettings, appsettings.{env}, environment variables).
/// </summary>
public sealed class AppDbContextDesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? Environments.Development;
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: true)
            .AddJsonFile($"appsettings.{environment}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(DataSetup.GetConnectionString(configuration))
            .Options;

        // No request at design time: the owner filter matches nothing (fail closed).
        return new AppDbContext(options, new CurrentUser());
    }
}
