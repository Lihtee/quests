namespace Quests.Api.Infrastructure.Auth;

/// <summary>Effective Firebase auth settings (from <c>FIREBASE_PROJECT_ID</c>).</summary>
public sealed record FirebaseAuthSettings(string ProjectId);
