using System.Net;
using System.Net.Http.Headers;

namespace Quests.Tests.Features.Identity;

/// <summary>Firebase Auth Emulator mode (<c>FIREBASE_AUTH_EMULATOR_HOST</c>): unsigned tokens, Development only.</summary>
public sealed class AuthEmulatorTests
{
    private const string EmulatorHost = "127.0.0.1:9099";

    [Fact]
    public async Task EmulatorMode_InDevelopment_AcceptsUnsignedToken()
    {
        await using var factory = ApiFactory.WithSettings(new Dictionary<string, string>
        {
            ["environment"] = "Development",
            ["FIREBASE_AUTH_EMULATOR_HOST"] = EmulatorHost,
        });

        var response = await GetMeAsync(factory, UnsignedToken());

        await AssertStatusAsync(response, HttpStatusCode.OK);
    }

    [Fact]
    public async Task EmulatorMode_StillValidatesAudience()
    {
        await using var factory = ApiFactory.WithSettings(new Dictionary<string, string>
        {
            ["environment"] = "Development",
            ["FIREBASE_AUTH_EMULATOR_HOST"] = EmulatorHost,
        });
        var token = TestTokens.Create($"uid-{Guid.NewGuid():N}", ApiFactory.AllowedEmail, audience: "other", unsigned: true);

        var response = await GetMeAsync(factory, token);

        await AssertStatusAsync(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public void EmulatorMode_OutsideDevelopment_RefusesToStart()
    {
        using var factory = ApiFactory.WithSettings(new Dictionary<string, string>
        {
            ["FIREBASE_AUTH_EMULATOR_HOST"] = EmulatorHost,
        });

        var error = Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
        Assert.Contains("FIREBASE_AUTH_EMULATOR_HOST", error.Message);
    }

    [Fact]
    public async Task WithoutEmulatorMode_UnsignedTokenIsRejected()
    {
        await using var factory = new ApiFactory();

        var response = await GetMeAsync(factory, UnsignedToken());

        await AssertStatusAsync(response, HttpStatusCode.Unauthorized);
    }

    private static string UnsignedToken() =>
        TestTokens.Create($"uid-{Guid.NewGuid():N}", ApiFactory.AllowedEmail, unsigned: true);

    private static async Task<HttpResponseMessage> GetMeAsync(ApiFactory factory, string token)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.GetAsync("/api/me");
    }

    private static async Task AssertStatusAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode == expected)
            return;

        var body = await response.Content.ReadAsStringAsync();
        Assert.Fail($"Expected {(int)expected}, got {(int)response.StatusCode}. Body: {body}");
    }
}
