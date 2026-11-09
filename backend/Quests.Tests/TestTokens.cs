using System.Security.Cryptography;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Quests.Tests;

/// <summary>Creates Firebase-like ID tokens signed with a test key that <see cref="ApiFactory"/> trusts.</summary>
public static class TestTokens
{
    public const string ProjectId = "test-project";
    public const string Issuer = $"https://securetoken.google.com/{ProjectId}";

    public static readonly SecurityKey SigningKey = NewKey();

    public static SecurityKey NewKey() => new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(32));

    public static string Create(
        string sub,
        string email,
        bool emailVerified = true,
        DateTime? expires = null,
        string audience = ProjectId,
        string issuer = Issuer,
        SecurityKey? signingKey = null,
        bool unsigned = false)
    {
        var expiresAt = expires ?? DateTime.UtcNow.AddHours(1);
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            IssuedAt = expiresAt.AddHours(-2),
            NotBefore = expiresAt.AddHours(-2),
            Expires = expiresAt,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = sub,
                ["email"] = email,
                ["email_verified"] = emailVerified,
            },
            // unsigned: alg "none", like Firebase Auth Emulator tokens
            SigningCredentials = unsigned
                ? null
                : new SigningCredentials(signingKey ?? SigningKey, SecurityAlgorithms.HmacSha256),
        };
        return new JsonWebTokenHandler().CreateToken(descriptor);
    }
}
