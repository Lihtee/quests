using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Quests.Api.Features.Identity;
using Quests.Api.Infrastructure.Data;

namespace Quests.Api.Features.Sync;

/// <summary>
/// SignalR "doorbell": server → client only. Each connection joins its user's group; after a sync that applied
/// changes, <see cref="SyncNotifier"/> sends <see cref="ChangedMethod"/> (no data) and clients run a delta sync.
/// Protected by the fallback policy (authenticated + allowlisted), like <c>/api/sync</c>.
/// </summary>
public sealed class SyncHub(AppDbContext db, ILogger<SyncHub> logger) : Hub
{
    public const string Path = "/api/hubs/sync";
    public const string ChangedMethod = "changed";

    public static string GroupFor(Guid userId) => $"user:{userId:N}";

    public override async Task OnConnectedAsync()
    {
        // The user row exists: provisioning middleware ran on the negotiate/connect request.
        var subject = Context.User?.FindFirst(IdentityClaims.Subject)?.Value;
        var userId = subject is null
            ? null
            : await db.Users.AsNoTracking()
                .Where(u => u.FirebaseUid == subject)
                .Select(u => (Guid?)u.Id)
                .FirstOrDefaultAsync(Context.ConnectionAborted);

        if (userId is null)
        {
            logger.LogWarning("Hub connection {ConnectionId} rejected: no user for subject", Context.ConnectionId);
            Context.Abort();
            return;
        }

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupFor(userId.Value), Context.ConnectionAborted);
        logger.LogDebug("Hub connection {ConnectionId} joined group for {UserId}", Context.ConnectionId, userId);
        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        logger.LogDebug("Hub connection {ConnectionId} disconnected ({ExceptionType})",
            Context.ConnectionId, exception?.GetType().Name ?? "clean");
        return base.OnDisconnectedAsync(exception);
    }
}
