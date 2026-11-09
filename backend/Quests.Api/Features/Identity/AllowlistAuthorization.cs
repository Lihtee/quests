using Microsoft.AspNetCore.Authorization;

namespace Quests.Api.Features.Identity;

/// <summary>
/// Fallback authorization policy: every endpoint requires an authenticated user with a verified,
/// allowlisted email (<c>ALLOWED_EMAILS</c>, comma-separated, case-insensitive).
/// </summary>
public static class AllowlistAuthorization
{
    private const string AllowedEmailsKey = "ALLOWED_EMAILS";
    private const string LoggerCategory = "Quests.Identity";

    public static IServiceCollection AddQuestsAuthorization(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton(AllowedEmails.Parse(configuration[AllowedEmailsKey]));
        services.AddSingleton<IAuthorizationHandler, AllowlistHandler>();
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .AddRequirements(new AllowlistRequirement())
                .Build());
        return services;
    }

    /// <summary>Logs the allowlist size once the app is built (never the emails).</summary>
    public static WebApplication LogQuestsAuthorizationSettings(this WebApplication app)
    {
        var allowed = app.Services.GetRequiredService<AllowedEmails>();
        var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger(LoggerCategory);

        if (allowed.Count == 0)
        {
            logger.LogWarning("{AllowedEmailsKey} is empty: nobody can sign in", AllowedEmailsKey);
            return app;
        }

        logger.LogInformation("Allowlist loaded with {Count} emails", allowed.Count);
        return app;
    }
}

internal sealed class AllowedEmails
{
    private readonly HashSet<string> _emails;

    private AllowedEmails(HashSet<string> emails) => _emails = emails;

    public int Count => _emails.Count;

    public bool Contains(string email) => _emails.Contains(email.Trim());

    public static AllowedEmails Parse(string? raw)
    {
        var emails = (raw ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return new AllowedEmails(emails);
    }
}

internal sealed class AllowlistRequirement : IAuthorizationRequirement;

internal sealed class AllowlistHandler(AllowedEmails allowed, ILogger<AllowlistHandler> logger)
    : AuthorizationHandler<AllowlistRequirement>
{
    protected override Task HandleRequirementAsync(AuthorizationHandlerContext context, AllowlistRequirement requirement)
    {
        // Unauthenticated users are rejected (401) by RequireAuthenticatedUser; nothing to do here.
        if (context.User.Identity?.IsAuthenticated != true)
            return Task.CompletedTask;

        var firebaseUid = context.User.FindFirst(IdentityClaims.Subject)?.Value;
        var email = context.User.FindFirst(IdentityClaims.Email)?.Value;
        var emailVerified = context.User.FindFirst(IdentityClaims.EmailVerified)?.Value;

        var denyReason = GetDenyReason(email, emailVerified);
        if (denyReason is not null)
        {
            logger.LogWarning("Access denied for {FirebaseUid}: {Reason}", firebaseUid, denyReason);
            logger.LogDebug("Denied email for {FirebaseUid}: {Email}", firebaseUid, email);
            context.Fail(new AuthorizationFailureReason(this, denyReason));
            return Task.CompletedTask;
        }

        logger.LogDebug("Access granted for {FirebaseUid}", firebaseUid);
        context.Succeed(requirement);
        return Task.CompletedTask;
    }

    private string? GetDenyReason(string? email, string? emailVerified)
    {
        if (!string.Equals(emailVerified, "true", StringComparison.OrdinalIgnoreCase))
            return "email_not_verified";

        if (string.IsNullOrWhiteSpace(email) || !allowed.Contains(email))
            return "not_allowlisted";

        return null;
    }
}
