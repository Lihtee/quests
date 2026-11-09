using Microsoft.EntityFrameworkCore;
using Quests.Api.Features.Identity;
using Quests.Api.Infrastructure.Data;

namespace Quests.Api.Features.Tasks;

/// <summary>
/// Task use cases for the signed-in user. Queries are owner-scoped by the <see cref="AppDbContext"/> filter.
/// Never logs task titles or notes.
/// </summary>
public sealed class TaskService(AppDbContext db, CurrentUser currentUser, TimeProvider clock, ILogger<TaskService> logger)
{
    private Guid UserId => currentUser.Id
        ?? throw new InvalidOperationException("CurrentUser is not set for an authorized request.");

    public async Task<IReadOnlyList<TaskItem>> ListAsync(CancellationToken ct)
    {
        var userId = UserId;
        // EF Core SQLite cannot ORDER BY DateTimeOffset, so sort in memory (per-user lists are small).
        var tasks = await db.Tasks.AsNoTracking().ToListAsync(ct);
        var sorted = Sort(tasks);
        logger.LogDebug("Listing tasks for {UserId}: {Count}", userId, sorted.Count);
        return sorted;
    }

    public async Task<TaskItem> CreateAsync(TaskRequest request, CancellationToken ct)
    {
        var userId = UserId;
        var task = TaskItem.Create(userId, request.Title!, request.Notes, request.DueAt, clock.GetUtcNow());
        db.Tasks.Add(task);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Task {TaskId} created for {UserId}", task.Id, userId);
        return task;
    }

    public async Task<TaskItem?> UpdateAsync(Guid id, TaskRequest request, CancellationToken ct)
    {
        var task = await FindAsync(id, ct);
        if (task is null)
            return null;

        task.Update(request.Title!, request.Notes, request.DueAt, clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Task {TaskId} updated for {UserId}", id, UserId);
        return task;
    }

    public async Task<TaskItem?> CompleteAsync(Guid id, CancellationToken ct)
    {
        var task = await FindAsync(id, ct);
        if (task is null)
            return null;

        task.Complete(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Task {TaskId} completed for {UserId}", id, UserId);
        return task;
    }

    public async Task<TaskItem?> ReopenAsync(Guid id, CancellationToken ct)
    {
        var task = await FindAsync(id, ct);
        if (task is null)
            return null;

        task.Reopen(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Task {TaskId} reopened for {UserId}", id, UserId);
        return task;
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct)
    {
        var task = await FindAsync(id, ct);
        if (task is null)
            return false;

        db.Tasks.Remove(task);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Task {TaskId} deleted for {UserId}", id, UserId);
        return true;
    }

    /// <summary>Open tasks first (due ascending, undated last, then created); completed after, newest completion first.</summary>
    public static List<TaskItem> Sort(IEnumerable<TaskItem> tasks)
    {
        var open = tasks.Where(t => !t.IsCompleted)
            .OrderBy(t => t.DueAt is null)
            .ThenBy(t => t.DueAt)
            .ThenBy(t => t.CreatedAt);
        var completed = tasks.Where(t => t.IsCompleted)
            .OrderByDescending(t => t.CompletedAt);
        return [.. open, .. completed];
    }

    private async Task<TaskItem?> FindAsync(Guid id, CancellationToken ct)
    {
        var userId = UserId;
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id, ct);
        if (task is null)
            logger.LogDebug("Task {TaskId} not found for {UserId}", id, userId);

        return task;
    }
}
