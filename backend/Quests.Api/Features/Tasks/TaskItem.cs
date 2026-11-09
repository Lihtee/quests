using Quests.Api.Infrastructure.Data;

namespace Quests.Api.Features.Tasks;

/// <summary>A user's task with an optional due date. Pure domain: the caller passes the current time.</summary>
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

    public bool IsCompleted => CompletedAt is not null;

    private TaskItem() { } // EF Core

    public static TaskItem Create(Guid ownerId, string title, string? notes, DateTimeOffset? dueAt, DateTimeOffset now)
    {
        if (ownerId == Guid.Empty)
            throw new ArgumentException("Owner id is required.", nameof(ownerId));

        var task = new TaskItem
        {
            Id = Guid.CreateVersion7(now),
            OwnerId = ownerId,
            CreatedAt = now,
        };
        task.Update(title, notes, dueAt, now);
        return task;
    }

    /// <summary>Replaces the editable fields.</summary>
    public void Update(string title, string? notes, DateTimeOffset? dueAt, DateTimeOffset now)
    {
        Title = NormalizeTitle(title);
        Notes = NormalizeNotes(notes);
        DueAt = dueAt?.ToUniversalTime();
        UpdatedAt = now;
    }

    /// <summary>Marks the task completed; no-op when already completed.</summary>
    public void Complete(DateTimeOffset now)
    {
        if (IsCompleted)
            return;

        CompletedAt = now;
        UpdatedAt = now;
    }

    /// <summary>Marks the task open again; no-op when already open.</summary>
    public void Reopen(DateTimeOffset now)
    {
        if (!IsCompleted)
            return;

        CompletedAt = null;
        UpdatedAt = now;
    }

    private static string NormalizeTitle(string title)
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
