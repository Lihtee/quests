using Quests.Api.Features.Tasks;

namespace Quests.Tests.Features.Tasks;

public sealed class TaskItemTests
{
    private static readonly Guid OwnerId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Create_TrimsTitle_NullsEmptyNotes_SetsTimestamps()
    {
        var task = TaskItem.Create(OwnerId, "  Buy milk  ", "   ", null, Now);

        Assert.NotEqual(Guid.Empty, task.Id);
        Assert.Equal(OwnerId, task.OwnerId);
        Assert.Equal("Buy milk", task.Title);
        Assert.Null(task.Notes);
        Assert.Null(task.DueAt);
        Assert.Equal(Now, task.CreatedAt);
        Assert.Equal(Now, task.UpdatedAt);
        Assert.Null(task.CompletedAt);
    }

    [Fact]
    public void Create_ConvertsDueAtToUtc()
    {
        var local = new DateTimeOffset(2026, 11, 10, 9, 30, 0, TimeSpan.FromHours(3));

        var task = TaskItem.Create(OwnerId, "Call", null, local, Now);

        Assert.Equal(TimeSpan.Zero, task.DueAt!.Value.Offset);
        Assert.Equal(local.UtcDateTime, task.DueAt.Value.UtcDateTime);
    }

    [Fact]
    public void Update_ChangesFieldsAndUpdatedAt_KeepsCreatedAt()
    {
        var task = TaskItem.Create(OwnerId, "Old", "old notes", null, Now);
        var later = Now.AddMinutes(5);
        var due = Now.AddDays(1);

        task.Update(" New ", "new notes", due, later);

        Assert.Equal("New", task.Title);
        Assert.Equal("new notes", task.Notes);
        Assert.Equal(due, task.DueAt);
        Assert.Equal(Now, task.CreatedAt);
        Assert.Equal(later, task.UpdatedAt);
    }

    [Fact]
    public void Complete_SetsCompletedAt_AndIsIdempotent()
    {
        var task = TaskItem.Create(OwnerId, "Task", null, null, Now);
        var first = Now.AddMinutes(1);

        task.Complete(first);
        task.Complete(Now.AddMinutes(2));

        Assert.Equal(first, task.CompletedAt);
        Assert.Equal(first, task.UpdatedAt);
    }

    [Fact]
    public void Reopen_ClearsCompletedAt_AndIsIdempotent()
    {
        var task = TaskItem.Create(OwnerId, "Task", null, null, Now);
        task.Reopen(Now.AddMinutes(1));
        Assert.Equal(Now, task.UpdatedAt); // no-op on an open task

        task.Complete(Now.AddMinutes(2));
        var reopenedAt = Now.AddMinutes(3);
        task.Reopen(reopenedAt);
        task.Reopen(Now.AddMinutes(4));

        Assert.Null(task.CompletedAt);
        Assert.Equal(reopenedAt, task.UpdatedAt);
    }

    [Fact]
    public void Create_RejectsBlankOrTooLongTitle()
    {
        Assert.ThrowsAny<ArgumentException>(() => TaskItem.Create(OwnerId, "  ", null, null, Now));
        Assert.Throws<ArgumentException>(() =>
            TaskItem.Create(OwnerId, new string('a', TaskItem.TitleMaxLength + 1), null, null, Now));
    }
}
