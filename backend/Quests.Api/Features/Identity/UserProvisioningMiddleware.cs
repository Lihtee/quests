using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Quests.Api.Infrastructure.Data;

namespace Quests.Api.Features.Identity;

/// <summary>
/// Runs after authorization: finds or creates the <see cref="User"/> for the token's <c>sub</c>
/// and fills <see cref="CurrentUser"/>. Skipped for anonymous endpoints and unauthenticated requests.
/// </summary>
public sealed class UserProvisioningMiddleware(RequestDelegate next, ILogger<UserProvisioningMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context, AppDbContext db, CurrentUser currentUser, TimeProvider clock)
    {
        if (!ShouldProvision(context))
        {
            await next(context);
            return;
        }

        var firebaseUid = context.User.FindFirst(IdentityClaims.Subject)?.Value;
        var email = context.User.FindFirst(IdentityClaims.Email)?.Value;
        if (string.IsNullOrWhiteSpace(firebaseUid) || string.IsNullOrWhiteSpace(email))
        {
            // Authorization requires an allowlisted email, and JwtBearer requires "sub"; this should not happen.
            logger.LogWarning("Authorized request without sub/email claims; responding 403");
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }

        var user = await FindOrCreateAsync(db, firebaseUid, email, clock, context.RequestAborted);
        currentUser.Set(user.Id, user.Email);
        logger.LogDebug("Resolved user {UserId} for {FirebaseUid}", user.Id, firebaseUid);

        await next(context);
    }

    private static bool ShouldProvision(HttpContext context)
    {
        if (context.User.Identity?.IsAuthenticated != true)
            return false;

        var endpoint = context.GetEndpoint();
        return endpoint?.Metadata.GetMetadata<IAllowAnonymous>() is null;
    }

    private async Task<User> FindOrCreateAsync(
        AppDbContext db, string firebaseUid, string email, TimeProvider clock, CancellationToken ct)
    {
        var existing = await db.Users.SingleOrDefaultAsync(u => u.FirebaseUid == firebaseUid, ct);
        if (existing is not null)
            return await SyncEmailAsync(db, existing, email, ct);

        var created = User.Create(firebaseUid, email, clock.GetUtcNow());
        db.Users.Add(created);
        try
        {
            await db.SaveChangesAsync(ct);
            logger.LogInformation("User provisioned {UserId}", created.Id);
            return created;
        }
        catch (DbUpdateException ex)
        {
            // First-sign-in race: a parallel request inserted the same FirebaseUid (unique index).
            logger.LogWarning(ex, "User insert conflict for {FirebaseUid}; re-reading existing row", firebaseUid);
            db.Entry(created).State = EntityState.Detached;

            var winner = await db.Users.SingleAsync(u => u.FirebaseUid == firebaseUid, ct);
            return await SyncEmailAsync(db, winner, email, ct);
        }
    }

    private async Task<User> SyncEmailAsync(AppDbContext db, User user, string email, CancellationToken ct)
    {
        if (!user.UpdateEmail(email))
            return user;

        await db.SaveChangesAsync(ct);
        logger.LogDebug("Updated email for user {UserId}", user.Id);
        return user;
    }
}
