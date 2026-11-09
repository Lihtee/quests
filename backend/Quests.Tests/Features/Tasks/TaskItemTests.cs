using Quests.Api.Features.Tasks;

namespace Quests.Tests.Features.Tasks;

public sealed class TaskItemTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    private static TaskFields Fields(
        string[] changed, string? title = null, string? notes = null, DateTimeOffset? dueAt = null,
        DateTimeOffset? completedAt = null) =>
        new(new HashSet<string>(changed), title, notes, dueAt, completedAt);

    private static TaskItem NewTask(string title = "Task", string? notes = null) =>
        TaskItem.Create(Guid.NewGuid(), OwnerId,
            Fields([TaskFields.TitleField, TaskFields.NotesField], title, notes), Now.AddHours(-1), 1, Now);

    [Fact]
    public void Create_UsesClientIdAndCreatedAt_TrimsTitle_NullsEmptyNotes()
    {
        var id = Guid.NewGuid();
        var createdAt = new DateTimeOffset(2026, 11, 9, 14, 0, 0, TimeSpan.FromHours(3));

        var task = TaskItem.Create(id, OwnerId, Fields([TaskFields.TitleField, TaskFields.NotesField], "  Buy milk  ", "   "),
            createdAt, 7, Now);

        Assert.Equal(id, task.Id);
        Assert.Equal(OwnerId, task.OwnerId);
        Assert.Equal("Buy milk", task.Title);
        Assert.Null(task.Notes);
        Assert.Equal(createdAt.UtcDateTime, task.CreatedAt.UtcDateTime);
        Assert.Equal(TimeSpan.Zero, task.CreatedAt.Offset);
        Assert.Equal(Now, task.UpdatedAt);
        Assert.Equal(7, task.Version);
        Assert.False(task.IsDeleted);
    }

    [Fact]
    public void Create_RequiresTitleField()
    {
        Assert.Throws<ArgumentException>(() =>
            TaskItem.Create(Guid.NewGuid(), OwnerId, Fields([TaskFields.NotesField], notes: "n"), Now, 1, Now));
    }

    [Fact]
    public void Apply_ChangesOnlyListedFields_SetsVersionAndUpdatedAt()
    {
        var task = NewTask("Old", "keep me");
        var later = Now.AddMinutes(5);
        var due = new DateTimeOffset(2026, 11, 10, 9, 30, 0, TimeSpan.FromHours(3));

        task.Apply(Fields([TaskFields.TitleField, TaskFields.DueAtField], " New ", notes: "ignored", dueAt: due), 2, later);

        Assert.Equal("New", task.Title);
        Assert.Equal("keep me", task.Notes);
        Assert.Equal(due.UtcDateTime, task.DueAt!.Value.UtcDateTime);
        Assert.Equal(TimeSpan.Zero, task.DueAt.Value.Offset);
        Assert.Equal(2, task.Version);
        Assert.Equal(later, task.UpdatedAt);
        Assert.Equal(Now.AddHours(-1), task.CreatedAt);
    }

    [Fact]
    public void Apply_CompletedAt_CompletesAndReopens()
    {
        var task = NewTask();
        var doneAt = Now.AddMinutes(1);

        task.Apply(Fields([TaskFields.CompletedAtField], completedAt: doneAt), 2, Now);
        Assert.Equal(doneAt, task.CompletedAt);

        task.Apply(Fields([TaskFields.CompletedAtField]), 3, Now);
        Assert.Null(task.CompletedAt);
    }

    [Fact]
    public void Apply_InvalidValue_ThrowsAndChangesNothing()
    {
        var task = NewTask("Title");

        Assert.ThrowsAny<ArgumentException>(() =>
            task.Apply(Fields([TaskFields.DueAtField, TaskFields.TitleField], "  ", dueAt: Now), 2, Now.AddMinutes(1)));
        Assert.Throws<ArgumentException>(() =>
            task.Apply(Fields([TaskFields.TitleField], new string('a', TaskItem.TitleMaxLength + 1)), 2, Now));
        Assert.Throws<ArgumentException>(() =>
            task.Apply(Fields([TaskFields.NotesField], notes: new string('n', TaskItem.NotesMaxLength + 1)), 2, Now));

        Assert.Equal("Title", task.Title);
        Assert.Null(task.DueAt);
        Assert.Equal(1, task.Version);
    }

    [Fact]
    public void MarkDeleted_SetsTombstone_KeepsData_AndIsIdempotent()
    {
        var task = NewTask("Title", "notes");
        var deletedAt = Now.AddMinutes(1);

        task.MarkDeleted(2, deletedAt);
        task.MarkDeleted(3, Now.AddMinutes(2));

        Assert.True(task.IsDeleted);
        Assert.Equal(deletedAt, task.DeletedAt);
        Assert.Equal(2, task.Version);
        Assert.Equal("Title", task.Title);
        Assert.Equal("notes", task.Notes);
    }

    [Fact]
    public void Apply_OnTombstone_RestoresWithOldData()
    {
        var task = NewTask("Title", "notes");
        task.MarkDeleted(2, Now);

        task.Apply(Fields([TaskFields.TitleField], "Renamed"), 3, Now.AddMinutes(1));

        Assert.False(task.IsDeleted);
        Assert.Equal("Renamed", task.Title);
        Assert.Equal("notes", task.Notes);
        Assert.Equal(3, task.Version);
    }
}
