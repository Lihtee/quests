using Microsoft.AspNetCore.SignalR;

namespace Quests.Api.Features.Sync;

/// <summary>
/// Rings the <see cref="SyncHub"/> doorbell for a user's connections. Best effort: a failed signal never fails the
/// sync, because the next delta sync catches up anyway.
/// </summary>
public sealed class SyncNotifier(IHubContext<SyncHub> hub, ILogger<SyncNotifier> logger)
{
    public async Task NotifyChangedAsync(Guid userId, string? excludeConnectionId, CancellationToken ct)
    {
        var group = SyncHub.GroupFor(userId);
        var clients = excludeConnectionId is null
            ? hub.Clients.Group(group)
            : hub.Clients.GroupExcept(group, [excludeConnectionId]);

        try
        {
            await clients.SendAsync(SyncHub.ChangedMethod, ct);
            logger.LogDebug("Signalled {UserId} connections (excluding {ExcludeConnectionId})",
                userId, excludeConnectionId ?? "none");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Sync signal for {UserId} failed", userId);
        }
    }
}
