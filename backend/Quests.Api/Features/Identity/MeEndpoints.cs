namespace Quests.Api.Features.Identity;

public static class MeEndpoints
{
    public static IEndpointRouteBuilder MapMeEndpoints(this IEndpointRouteBuilder app)
    {
        // Protected by the fallback policy (authenticated + allowlisted); CurrentUser is set by the middleware.
        app.MapGet("/api/me", (CurrentUser currentUser, ILoggerFactory loggerFactory) =>
        {
            if (currentUser.Id is not { } id || currentUser.Email is not { } email)
                throw new InvalidOperationException("CurrentUser is not set for an authorized request.");

            loggerFactory.CreateLogger("Quests.Identity").LogDebug("GET /api/me for {UserId}", id);
            return TypedResults.Ok(new MeResponse(id, email));
        });
        return app;
    }
}
