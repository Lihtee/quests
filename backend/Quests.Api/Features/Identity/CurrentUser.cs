namespace Quests.Api.Features.Identity;

/// <summary>
/// The signed-in user for the current request (scoped). Set once by <see cref="UserProvisioningMiddleware"/>;
/// <see cref="Id"/> is null outside an authorized request (e.g. startup migrations).
/// </summary>
public sealed class CurrentUser
{
    public Guid? Id { get; private set; }
    public string? Email { get; private set; }

    public void Set(Guid id, string email)
    {
        if (Id is not null)
            throw new InvalidOperationException("CurrentUser is already set for this request.");

        Id = id;
        Email = email;
    }
}
