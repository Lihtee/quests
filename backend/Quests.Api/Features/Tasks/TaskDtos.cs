namespace Quests.Api.Features.Tasks;

/// <summary>Body for creating a task and for a full update (PUT).</summary>
public sealed record TaskRequest(string? Title, string? Notes, DateTimeOffset? DueAt)
{
    /// <summary>Validates the request at the API boundary; an empty dictionary means valid.</summary>
    public static Dictionary<string, string[]> Validate(TaskRequest request)
    {
        var errors = new Dictionary<string, string[]>();

        var title = request.Title?.Trim();
        if (string.IsNullOrEmpty(title))
            errors[nameof(Title)] = ["Title is required."];
        else if (title.Length > TaskItem.TitleMaxLength)
            errors[nameof(Title)] = [$"Title must be at most {TaskItem.TitleMaxLength} characters."];

        if (request.Notes is { Length: > TaskItem.NotesMaxLength })
            errors[nameof(Notes)] = [$"Notes must be at most {TaskItem.NotesMaxLength} characters."];

        return errors;
    }
}

public sealed record TaskResponse(
    Guid Id,
    string Title,
    string? Notes,
    DateTimeOffset? DueAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public static TaskResponse From(TaskItem task) =>
        new(task.Id, task.Title, task.Notes, task.DueAt, task.CompletedAt, task.CreatedAt, task.UpdatedAt);
}
