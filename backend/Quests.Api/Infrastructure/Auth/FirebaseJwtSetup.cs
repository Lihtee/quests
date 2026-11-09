using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace Quests.Api.Infrastructure.Auth;

/// <summary>Firebase ID token (JWT) validation via JwtBearer.</summary>
public static class FirebaseJwtSetup
{
    private const string ProjectIdKey = "FIREBASE_PROJECT_ID";
    private const string LoggerCategory = "Quests.Auth";

    public static IServiceCollection AddQuestsAuthentication(this IServiceCollection services, IConfiguration configuration)
    {
        var projectId = configuration[ProjectIdKey];
        if (string.IsNullOrWhiteSpace(projectId))
        {
            throw new InvalidOperationException(
                $"'{ProjectIdKey}' is missing. Set it via the '{ProjectIdKey}' environment variable " +
                "to your Firebase project id, e.g. 'quests-12345'.");
        }

        projectId = projectId.Trim();
        var issuer = $"https://securetoken.google.com/{projectId}";

        services.AddSingleton(new FirebaseAuthSettings(projectId));
        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.Authority = issuer;
                options.MapInboundClaims = false; // keep "sub", "email", "email_verified" as-is
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = issuer,
                    ValidateAudience = true,
                    ValidAudience = projectId,
                    ValidateLifetime = true,
                };
                options.Events = new JwtBearerEvents
                {
                    OnAuthenticationFailed = context =>
                    {
                        CreateLogger(context.HttpContext).LogDebug(
                            "JWT authentication failed: {ExceptionType}: {ExceptionMessage}",
                            context.Exception.GetType().Name, context.Exception.Message);
                        return Task.CompletedTask;
                    },
                    OnChallenge = context =>
                    {
                        CreateLogger(context.HttpContext).LogDebug(
                            "JWT challenge for {RequestPath}", context.Request.Path);
                        return Task.CompletedTask;
                    },
                };
            });

        return services;
    }

    /// <summary>Logs the effective auth settings once the app is built.</summary>
    public static WebApplication LogQuestsAuthenticationSettings(this WebApplication app)
    {
        var settings = app.Services.GetRequiredService<FirebaseAuthSettings>();
        app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(LoggerCategory)
            .LogDebug("Firebase JWT auth configured for project {ProjectId}", settings.ProjectId);
        return app;
    }

    private static ILogger CreateLogger(HttpContext httpContext) =>
        httpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(LoggerCategory);
}
