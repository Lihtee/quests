namespace Quests.Api.Features.Identity;

/// <summary>An allowlisted account, created on first sign-in. Owned data references <see cref="Id"/>.</summary>
public sealed class User
{
    public Guid Id { get; private set; }
    public string FirebaseUid { get; private set; } = "";
    public string Email { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }

    private User() { } // EF Core

    public static User Create(string firebaseUid, string email, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(firebaseUid);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);

        return new User
        {
            Id = Guid.CreateVersion7(now),
            FirebaseUid = firebaseUid,
            Email = email,
            CreatedAt = now,
        };
    }

    /// <summary>Updates the email; returns true when it changed.</summary>
    public bool UpdateEmail(string email)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        if (string.Equals(Email, email, StringComparison.Ordinal))
            return false;

        Email = email;
        return true;
    }
}
