using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Quests.Api.Infrastructure.Data;

namespace Quests.Tests.Features.Identity;

/// <summary>Firebase JWT validation, email allowlist and user provisioning via <c>GET /api/me</c>.</summary>
public sealed class AuthTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string MePath = "/api/me";

    [Fact]
    public async Task NoToken_Returns401ProblemDetails()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(MePath);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized);
    }

    public static TheoryData<string> InvalidTokenCases => ["bad-signature", "wrong-issuer", "wrong-audience", "expired"];

    [Theory]
    [MemberData(nameof(InvalidTokenCases))]
    public async Task InvalidToken_Returns401AndCreatesNoUser(string invalidCase)
    {
        var sub = NewSub();
        var token = invalidCase switch
        {
            "bad-signature" => TestTokens.Create(sub, ApiFactory.AllowedEmail, signingKey: TestTokens.NewKey()),
            "wrong-issuer" => TestTokens.Create(sub, ApiFactory.AllowedEmail, issuer: "https://securetoken.google.com/other"),
            "wrong-audience" => TestTokens.Create(sub, ApiFactory.AllowedEmail, audience: "other"),
            "expired" => TestTokens.Create(sub, ApiFactory.AllowedEmail, expires: DateTime.UtcNow.AddHours(-1)),
            _ => throw new ArgumentOutOfRangeException(nameof(invalidCase), invalidCase, null),
        };

        var response = await GetMeAsync(token);

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized);
        Assert.Equal(0, await CountUsersAsync(sub));
    }

    [Fact]
    public async Task EmailNotVerified_Returns403AndCreatesNoUser()
    {
        var sub = NewSub();

        var response = await GetMeAsync(TestTokens.Create(sub, ApiFactory.AllowedEmail, emailVerified: false));

        await AssertProblemAsync(response, HttpStatusCode.Forbidden);
        Assert.Equal(0, await CountUsersAsync(sub));
    }

    [Fact]
    public async Task EmailNotAllowlisted_Returns403AndCreatesNoUser()
    {
        var sub = NewSub();

        var response = await GetMeAsync(TestTokens.Create(sub, "stranger@test.local"));

        await AssertProblemAsync(response, HttpStatusCode.Forbidden);
        Assert.Equal(0, await CountUsersAsync(sub));
    }

    [Fact]
    public async Task AllowlistedUser_FirstCallCreatesUser_RepeatCallReusesIt()
    {
        var sub = NewSub();
        var token = TestTokens.Create(sub, ApiFactory.AllowedEmail);

        var first = await GetMeJsonAsync(token);
        var second = await GetMeJsonAsync(token);

        Assert.Equal(ApiFactory.AllowedEmail, first.GetProperty("email").GetString());
        var id = first.GetProperty("id").GetGuid();
        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal(id, second.GetProperty("id").GetGuid());
        Assert.Equal(1, await CountUsersAsync(sub));
    }

    [Fact]
    public async Task AllowlistMatch_IsCaseInsensitive()
    {
        var sub = NewSub();

        var me = await GetMeJsonAsync(TestTokens.Create(sub, "ALLOWED@test.local"));

        Assert.Equal("ALLOWED@test.local", me.GetProperty("email").GetString());
        Assert.Equal(1, await CountUsersAsync(sub));
    }

    [Fact]
    public async Task Health_WithoutToken_Returns200()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static string NewSub() => $"uid-{Guid.NewGuid():N}";

    private async Task<HttpResponseMessage> GetMeAsync(string token)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client.GetAsync(MePath);
    }

    private async Task<JsonElement> GetMeJsonAsync(string token)
    {
        var response = await GetMeAsync(token);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"Expected 200, got {(int)response.StatusCode}. Body: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private async Task<int> CountUsersAsync(string firebaseUid)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.Users.CountAsync(u => u.FirebaseUid == firebaseUid);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected,
            $"Expected {(int)expected}, got {(int)response.StatusCode}. Body: {body}");
        Assert.True(response.Content.Headers.ContentType?.MediaType == "application/problem+json",
            $"Expected application/problem+json, got '{response.Content.Headers.ContentType}'. Body: {body}");
    }
}
