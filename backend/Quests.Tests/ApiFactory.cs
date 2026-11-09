using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace Quests.Tests;

/// <summary>
/// Hosts the API in-memory with a unique temp SQLite file that is deleted on dispose.
/// JWTs are validated with <see cref="TestTokens.SigningKey"/> instead of Google's keys (issuer/audience unchanged).
/// Optionally serves a temp web root (<see cref="IndexHtml"/>, <see cref="AppJs"/>) and applies extra settings.
/// </summary>
public class ApiFactory : WebApplicationFactory<Program>
{
    public const string AllowedEmail = "allowed@test.local";
    public const string IndexHtml = "<!doctype html><title>quests-test-index</title>";
    public const string AppJs = "console.log('quests-test-asset');";

    private readonly string _databasePath =
        Path.Combine(Path.GetTempPath(), $"quests-tests-{Guid.NewGuid():N}.db");

    private readonly string? _webRoot;
    private readonly IReadOnlyDictionary<string, string> _settings;

    public ApiFactory() : this(withWebRoot: false, settings: null)
    {
    }

    protected ApiFactory(bool withWebRoot, IReadOnlyDictionary<string, string>? settings)
    {
        _settings = settings ?? new Dictionary<string, string>();
        if (withWebRoot)
            _webRoot = CreateWebRoot();
    }

    /// <summary>Creates a factory with extra host settings (for example <c>LOG_FILE</c>).</summary>
    public static ApiFactory WithSettings(IReadOnlyDictionary<string, string> settings) =>
        new(withWebRoot: false, settings);

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", $"Data Source={_databasePath}");
        builder.UseSetting("FIREBASE_PROJECT_ID", TestTokens.ProjectId);
        builder.UseSetting("ALLOWED_EMAILS", AllowedEmail);
        foreach (var (key, value) in _settings)
            builder.UseSetting(key, value);
        if (_webRoot is not null)
            builder.UseWebRoot(_webRoot);

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
        if (_webRoot is not null && Directory.Exists(_webRoot))
            Directory.Delete(_webRoot, recursive: true);
    }

    private static string CreateWebRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), $"quests-tests-web-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "assets"));
        File.WriteAllText(Path.Combine(root, "index.html"), IndexHtml);
        File.WriteAllText(Path.Combine(root, "assets", "app.js"), AppJs);
        return root;
    }
}
