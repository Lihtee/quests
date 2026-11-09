using Microsoft.EntityFrameworkCore;
using Quests.Api.Features.Identity;
using Quests.Api.Features.Tasks;
using Quests.Api.Infrastructure.Data;

namespace Quests.Api.Features.Sync;

/// <summary>
/// Delta sync for the signed-in user: applies pushed changes in order, then returns tasks changed since the
/// client's version. Changes carry only the fields they changed, so concurrent edits merge per field (last
/// arrival wins per field). A delete wins only if the device had seen the latest version. Never logs task content.
/// </summary>
public sealed class SyncService(AppDbContext db, CurrentUser currentUser, TimeProvider clock, ILogger<SyncService> logger)
{
    private enum Outcome { Applied, Ignored, Rejected, NoOp }

    private Guid UserId => currentUser.Id
        ?? throw new InvalidOperationException("CurrentUser is not set for an authorized request.");

    public async Task<SyncResponse> SyncAsync(SyncRequest request, CancellationToken ct)
    {
        var userId = UserId;
        var changes = request.Changes ?? [];
        logger.LogDebug("Sync start for {UserId}: since {Since}, {Count} changes", userId, request.Since, changes.Length);

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var batch = new Batch(userId, await ReserveVersionsAsync(userId, changes.Length, ct), clock.GetUtcNow());
        var known = await LoadTouchedTasksAsync(changes, ct);

        foreach (var change in changes)
        {
            var outcome = Apply(change, known, batch);
            batch.Count(outcome);
            logger.LogDebug("Change {Op} {TaskId}: {Outcome}", change.Op, change.Id, outcome);
        }

        await db.SaveChangesAsync(ct);
        var tasks = await LoadResponseTasksAsync(request.Since, batch.ReturnIds, ct);
        var version = await db.Users.Where(u => u.Id == userId).Select(u => u.SyncVersion).SingleAsync(ct);
        await transaction.CommitAsync(ct);

        logger.LogInformation(
            "Sync for {UserId}: {Applied} applied, {Ignored} ignored, {Rejected} rejected, {Returned} returned, version {Version}",
            userId, batch.Applied, batch.Ignored, batch.Rejected.Count, tasks.Length, version);
        return new SyncResponse(version, tasks, [.. batch.Rejected]);
    }

    /// <summary>
    /// Takes one version per change up front. The UPDATE also takes the SQLite write lock, so concurrent syncs of
    /// the same user run one after another. Returns the last version before the reserved range.
    /// </summary>
    private async Task<long> ReserveVersionsAsync(Guid userId, int count, CancellationToken ct)
    {
        var users = db.Users.Where(u => u.Id == userId);
        if (count > 0)
            await users.ExecuteUpdateAsync(s => s.SetProperty(u => u.SyncVersion, u => u.SyncVersion + count), ct);

        var end = await users.Select(u => u.SyncVersion).SingleAsync(ct);
        return end - count;
    }

    /// <summary>
    /// Loads every task a change refers to, including other users' tasks: the owner filter is bypassed only to
    /// detect (and reject) a change to a task id owned by someone else. Those tasks are never modified or returned.
    /// </summary>
    private async Task<Dictionary<Guid, TaskItem>> LoadTouchedTasksAsync(TaskChange[] changes, CancellationToken ct)
    {
        var ids = changes.Select(c => c.Id).Distinct().ToList();
        if (ids.Count == 0)
            return [];

        return await db.Tasks.IgnoreQueryFilters().Where(t => ids.Contains(t.Id)).ToDictionaryAsync(t => t.Id, ct);
    }

    private Outcome Apply(TaskChange change, Dictionary<Guid, TaskItem> known, Batch batch)
    {
        known.TryGetValue(change.Id, out var task);
        if (task is not null && task.OwnerId != batch.UserId)
        {
            logger.LogWarning("Change for task {TaskId} by {UserId} rejected: owned by another user", change.Id, batch.UserId);
            batch.Reject(change.Id, RejectedChange.Forbidden);
            return Outcome.Rejected;
        }

        if (change.FindError() is { } error)
            return Reject(change, task, batch, error);

        return change.IsDelete ? ApplyDelete(change, task, batch) : ApplyUpsert(change, task, known, batch);
    }

    private static Outcome ApplyDelete(TaskChange change, TaskItem? task, Batch batch)
    {
        if (task is null || task.IsDeleted)
            return Outcome.NoOp;

        // Preserve beats delete: the task changed after the device last saw it.
        if (task.Version > change.BaseVersion)
        {
            batch.ReturnIds.Add(task.Id);
            return Outcome.Ignored;
        }

        task.MarkDeleted(batch.NextVersion(), batch.Now);
        return Outcome.Applied;
    }

    private Outcome ApplyUpsert(TaskChange change, TaskItem? task, Dictionary<Guid, TaskItem> known, Batch batch)
    {
        var fields = change.ToFields();
        if (task is not null)
        {
            task.Apply(fields, batch.NextVersion(), batch.Now);
            return Outcome.Applied;
        }

        if (!fields.Has(TaskFields.TitleField))
            return Reject(change, task, batch, "new task without title");

        var created = TaskItem.Create(
            change.Id, batch.UserId, fields, change.CreatedAt ?? batch.Now, batch.NextVersion(), batch.Now);
        db.Tasks.Add(created);
        known[created.Id] = created;
        return Outcome.Applied;
    }

    private Outcome Reject(TaskChange change, TaskItem? task, Batch batch, string error)
    {
        logger.LogDebug("Change {Op} {TaskId} invalid: {Error}", change.Op, change.Id, error);
        batch.Reject(change.Id, RejectedChange.Invalid);
        if (task is not null)
            batch.ReturnIds.Add(task.Id);

        return Outcome.Rejected;
    }

    /// <summary>
    /// Own tasks changed after <paramref name="since"/> (a first pull skips tombstones), plus the current state of
    /// tasks whose change was ignored or rejected.
    /// </summary>
    private async Task<TaskDto[]> LoadResponseTasksAsync(long since, HashSet<Guid> returnIds, CancellationToken ct)
    {
        var ids = returnIds.ToList();
        var tasks = await db.Tasks.AsNoTracking()
            .Where(t => (t.Version > since && (since > 0 || t.DeletedAt == null)) || ids.Contains(t.Id))
            .ToListAsync(ct);
        return [.. tasks.Select(TaskDto.From)];
    }

    /// <summary>State of one sync request: reserved versions, outcomes, and tasks to return.</summary>
    private sealed class Batch(Guid userId, long lastVersionBefore, DateTimeOffset now)
    {
        private long _version = lastVersionBefore;

        public Guid UserId { get; } = userId;
        public DateTimeOffset Now { get; } = now;
        public HashSet<Guid> ReturnIds { get; } = [];
        public List<RejectedChange> Rejected { get; } = [];
        public int Applied { get; private set; }
        public int Ignored { get; private set; }

        public long NextVersion() => ++_version;

        public void Reject(Guid id, string reason) => Rejected.Add(new RejectedChange(id, reason));

        public void Count(Outcome outcome)
        {
            if (outcome == Outcome.Applied)
                Applied++;
            else if (outcome == Outcome.Ignored)
                Ignored++;
        }
    }
}
