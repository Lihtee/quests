using Microsoft.AspNetCore.Http.HttpResults;

namespace Quests.Api.Features.Sync;

public static class SyncEndpoints
{
    public static IEndpointRouteBuilder MapSyncEndpoints(this IEndpointRouteBuilder app)
    {
        // Protected by the fallback policy (authenticated + allowlisted). A malformed body → 400 ProblemDetails.
        app.MapPost("/api/sync", async Task<Results<Ok<SyncResponse>, ValidationProblem>> (
            SyncRequest request, SyncService service, CancellationToken ct) =>
        {
            if (request.Since < 0)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(SyncRequest.Since)] = ["Since must not be negative."],
                });

            return TypedResults.Ok(await service.SyncAsync(request, ct));
        });

        return app;
    }
}
