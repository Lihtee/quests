using Microsoft.AspNetCore.Http.HttpResults;

namespace Quests.Api.Features.Sync;

public static class SyncEndpoints
{
    /// <summary>The caller's <see cref="SyncHub"/> connection id; that connection is skipped by the signal.</summary>
    public const string ConnectionIdHeader = "X-Sync-Connection-Id";

    public static IEndpointRouteBuilder MapSyncEndpoints(this IEndpointRouteBuilder app)
    {
        // Protected by the fallback policy (authenticated + allowlisted). A malformed body → 400 ProblemDetails.
        app.MapPost("/api/sync", async Task<Results<Ok<SyncResponse>, ValidationProblem>> (
            SyncRequest request, HttpRequest http, SyncService service, CancellationToken ct) =>
        {
            if (request.Since < 0)
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    [nameof(SyncRequest.Since)] = ["Since must not be negative."],
                });

            var connectionId = http.Headers[ConnectionIdHeader].ToString().Trim();
            return TypedResults.Ok(await service.SyncAsync(request, connectionId.Length > 0 ? connectionId : null, ct));
        });

        return app;
    }
}
