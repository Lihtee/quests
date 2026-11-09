using Quests.Api.Features.Tasks;

namespace Quests.Api.Features.Sync;

/// <summary>Body of <c>POST /api/sync</c>: local changes to push, and the last server version the client has.</summary>
public sealed record SyncRequest(long Since, TaskChange[]? Changes);

/// <summary>
/// One local change. <see cref="Op"/> is <c>upsert</c> or <c>delete</c>. For an upsert only the fields named in
/// <see cref="Changed"/> are applied. <see cref="BaseVersion"/> is the task version the device had seen.
/// </summary>
public sealed record TaskChange(
    Guid Id,
    string? Op,
    long BaseVersion,
    string[]? Changed,
    string? Title,
    string? Notes,
    DateTimeOffset? DueAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? CreatedAt)
{
    public const string UpsertOp = "upsert";
    public const string DeleteOp = "delete";

    public bool IsDelete => Op == DeleteOp;

    public TaskFields ToFields() =>
        new(new HashSet<string>(Changed ?? [], StringComparer.Ordinal), Title, Notes, DueAt, CompletedAt);

    /// <summary>Returns why the change is malformed (no user content), or null when it is well-formed.</summary>
    public string? FindError()
    {
        if (Id == Guid.Empty)
            return "missing id";
        if (Op is not (UpsertOp or DeleteOp))
            return "unknown op";
        if (IsDelete)
            return null;
        if (Changed is not { Length: > 0 })
            return "no changed fields";
        if (Changed.FirstOrDefault(f => !TaskFields.Known.Contains(f)) is { } unknown)
            return $"unknown field '{unknown}'";

        var invalid = TaskValidation.InvalidFields(ToFields());
        return invalid.Count == 0 ? null : $"invalid {string.Join(", ", invalid)}";
    }
}

/// <summary>Response of <c>POST /api/sync</c>. <see cref="Version"/> is the client's next <c>since</c>.</summary>
public sealed record SyncResponse(long Version, TaskDto[] Tasks, RejectedChange[] Rejected);

/// <summary>A pushed change the server did not apply. <see cref="Reason"/> is a code: <c>invalid</c> or <c>forbidden</c>.</summary>
public sealed record RejectedChange(Guid Id, string Reason)
{
    public const string Invalid = "invalid";
    public const string Forbidden = "forbidden";
}

/// <summary>Server state of a task. A non-null <see cref="DeletedAt"/> means it is deleted (tombstone).</summary>
public sealed record TaskDto(
    Guid Id,
    string Title,
    string? Notes,
    DateTimeOffset? DueAt,
    DateTimeOffset? CompletedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    long Version,
    DateTimeOffset? DeletedAt)
{
    public static TaskDto From(TaskItem task) =>
        new(task.Id, task.Title, task.Notes, task.DueAt, task.CompletedAt, task.CreatedAt, task.UpdatedAt,
            task.Version, task.DeletedAt);
}
