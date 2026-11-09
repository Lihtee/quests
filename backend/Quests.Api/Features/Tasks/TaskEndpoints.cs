using Microsoft.AspNetCore.Http.HttpResults;

namespace Quests.Api.Features.Tasks;

public static class TaskEndpoints
{
    public static IEndpointRouteBuilder MapTaskEndpoints(this IEndpointRouteBuilder app)
    {
        // Protected by the fallback policy (authenticated + allowlisted).
        var group = app.MapGroup("/api/tasks");

        group.MapGet("/", async (TaskService service, CancellationToken ct) =>
        {
            var tasks = await service.ListAsync(ct);
            return TypedResults.Ok(tasks.Select(TaskResponse.From).ToArray());
        });

        group.MapPost("/", async Task<Results<Created<TaskResponse>, ValidationProblem>> (
            TaskRequest request, TaskService service, ILogger<TaskService> logger, CancellationToken ct) =>
        {
            if (Invalid(request, logger) is { } problem)
                return problem;

            var task = await service.CreateAsync(request, ct);
            return TypedResults.Created($"/api/tasks/{task.Id}", TaskResponse.From(task));
        });

        group.MapPut("/{id:guid}", async Task<Results<Ok<TaskResponse>, ValidationProblem, ProblemHttpResult>> (
            Guid id, TaskRequest request, TaskService service, ILogger<TaskService> logger, CancellationToken ct) =>
        {
            if (Invalid(request, logger) is { } problem)
                return problem;

            var task = await service.UpdateAsync(id, request, ct);
            return task is null ? NotFound() : TypedResults.Ok(TaskResponse.From(task));
        });

        group.MapPost("/{id:guid}/complete", async Task<Results<Ok<TaskResponse>, ProblemHttpResult>> (
            Guid id, TaskService service, CancellationToken ct) => OkOrNotFound(await service.CompleteAsync(id, ct)));

        group.MapPost("/{id:guid}/reopen", async Task<Results<Ok<TaskResponse>, ProblemHttpResult>> (
            Guid id, TaskService service, CancellationToken ct) => OkOrNotFound(await service.ReopenAsync(id, ct)));

        group.MapDelete("/{id:guid}", async Task<Results<NoContent, ProblemHttpResult>> (
            Guid id, TaskService service, CancellationToken ct) =>
            await service.DeleteAsync(id, ct) ? TypedResults.NoContent() : NotFound());

        return app;
    }

    private static ValidationProblem? Invalid(TaskRequest request, ILogger logger)
    {
        var errors = TaskRequest.Validate(request);
        if (errors.Count == 0)
            return null;

        logger.LogDebug("Task request rejected; invalid fields: {Fields}", string.Join(", ", errors.Keys));
        return TypedResults.ValidationProblem(errors);
    }

    private static Results<Ok<TaskResponse>, ProblemHttpResult> OkOrNotFound(TaskItem? task) =>
        task is null ? NotFound() : TypedResults.Ok(TaskResponse.From(task));

    private static ProblemHttpResult NotFound() =>
        TypedResults.Problem(statusCode: StatusCodes.Status404NotFound, title: "Task not found.");
}
