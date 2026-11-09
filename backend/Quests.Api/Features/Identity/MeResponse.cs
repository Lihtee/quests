namespace Quests.Api.Features.Identity;

/// <summary>Response of <c>GET /api/me</c>.</summary>
public sealed record MeResponse(Guid Id, string Email);
