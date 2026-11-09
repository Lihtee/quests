namespace Quests.Api.Infrastructure.Data;

/// <summary>
/// Data owned by a user. <see cref="AppDbContext"/> applies an <c>OwnerId</c> query filter to every such entity.
/// </summary>
public interface IOwnedEntity
{
    Guid OwnerId { get; }
}
