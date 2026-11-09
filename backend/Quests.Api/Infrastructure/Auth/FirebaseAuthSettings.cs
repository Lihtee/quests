namespace Quests.Api.Infrastructure.Auth;

/// <summary>
/// Effective Firebase auth settings (from <c>FIREBASE_PROJECT_ID</c> and, in Development only,
/// <c>FIREBASE_AUTH_EMULATOR_HOST</c>). <see cref="EmulatorHost"/> is null outside emulator mode.
/// </summary>
public sealed record FirebaseAuthSettings(string ProjectId, string? EmulatorHost)
{
    public bool UsesEmulator => EmulatorHost is not null;
}
