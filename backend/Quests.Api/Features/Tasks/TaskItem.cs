using Quests.Api.Infrastructure.Data;

namespace Quests.Api.Features.Tasks;

/// <summary>
/// A user's task with an optional due date. Pure domain: the caller passes the current time and the
/// sync version. Deletes are soft (tombstone) so a later edit can restore the task with its data.
/// </summary>
public sealed class TaskItem : IOwnedEntity
{
    public const int TitleMaxLength = 200;
    public const int NotesMaxLength = 4000;

    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Title { get; private set; } = "";
    public string? Notes { get; private set; }
    public DateTimeOffset? DueAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public DateTimeOffset? DeletedAt { get; private set; }

    /// <summary>The owner's sync version at the last applied change.</summary>
    public long Version { get; private set; }

    public bool IsCompleted => CompletedAt is not null;
    public bool IsDeleted => DeletedAt is not null;

    private TaskItem() { } // EF Core

    /// <summary>Creates a task with a client-generated id. <paramref name="fields"/> must include the title.</summary>
    public static TaskItem Create(
        Guid id, Guid ownerId, TaskFields fields, DateTimeOffset createdAt, long version, DateTimeOffset now)
    {
        if (id == Guid.Empty)
            throw new ArgumentException("Task id is required.", nameof(id));
        if (ownerId == Guid.Empty)
            throw new ArgumentException("Owner id is required.", nameof(ownerId));
        if (!fields.Has(TaskFields.TitleField))
            throw new ArgumentException("A new task needs a title.", nameof(fields));

        var task = new TaskItem
        {
            Id = id,
            OwnerId = ownerId,
            CreatedAt = createdAt.ToUniversalTime(),
        };
        task.Apply(fields, version, now);
        return task;
    }

    /// <summary>
    /// Applies the listed fields and keeps the others. Restores the task when it was deleted.
    /// Validates everything before changing anything.
    /// </summary>
    public void Apply(TaskFields fields, long version, DateTimeOffset now)
    {
        var title = fields.Has(TaskFields.TitleField) ? NormalizeTitle(fields.Title) : Title;
        var notes = fields.Has(TaskFields.NotesField) ? NormalizeNotes(fields.Notes) : Notes;
        var dueAt = fields.Has(TaskFields.DueAtField) ? fields.DueAt?.ToUniversalTime() : DueAt;
        var completedAt = fields.Has(TaskFields.CompletedAtField) ? fields.CompletedAt?.ToUniversalTime() : CompletedAt;

        Title = title;
        Notes = notes;
        DueAt = dueAt;
        CompletedAt = completedAt;
        DeletedAt = null;
        Version = version;
        UpdatedAt = now;
    }

    /// <summary>Marks the task deleted (tombstone); no-op when already deleted.</summary>
    public void MarkDeleted(long version, DateTimeOffset now)
    {
        if (IsDeleted)
            return;

        DeletedAt = now;
        Version = version;
        UpdatedAt = now;
    }

    private static string NormalizeTitle(string? title)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        var trimmed = title.Trim();
        if (trimmed.Length > TitleMaxLength)
            throw new ArgumentException($"Title must be at most {TitleMaxLength} characters.", nameof(title));

        return trimmed;
    }

    private static string? NormalizeNotes(string? notes)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return null;

        if (notes.Length > NotesMaxLength)
            throw new ArgumentException($"Notes must be at most {NotesMaxLength} characters.", nameof(notes));

        return notes;
    }
}
