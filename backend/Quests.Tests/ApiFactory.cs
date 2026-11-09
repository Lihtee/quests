using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Quests.Tests;

/// <summary>
/// Hosts the API in-memory with a unique temp SQLite file that is deleted on dispose.
/// JWTs are validated with <see cref="TestTokens.SigningKey"/> instead of Google's keys (issuer/audience unchanged).
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string AllowedEmail = "allowed@test.local";

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"quests-tests-{Guid.NewGuid():N}.db");

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={_databasePath}");
        builder.UseSetting("FIREBASE_PROJECT_ID", TestTokens.ProjectId);
        builder.UseSetting("ALLOWED_EMAILS", AllowedEmail);

        builder.ConfigureServices(services =>
            services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
            {
                // No OpenID metadata download: trust the test key only.
                options.Authority = null;
                options.ConfigurationManager = null;
                options.TokenValidationParameters.IssuerSigningKey = TestTokens.SigningKey;
            }));
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
