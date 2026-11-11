using System.Net;
using System.Net.Http.Headers;

namespace Quests.Tests.Infrastructure;

/// <summary>CORS for the Android app origin (<c>CORS_ORIGINS</c>): preflight without a token, allowed and foreign origins.</summary>
public sealed class CorsTests
{
    private const string AppOrigin = "https://localhost";
    private const string ForeignOrigin = "https://evil.example";
    private const string AllowOriginHeader = "Access-Control-Allow-Origin";

    private static ApiFactory CreateFactory() =>
        ApiFactory.WithSettings(new Dictionary<string, string> { ["CORS_ORIGINS"] = AppOrigin });

    [Theory]
    [InlineData("/api/sync")]
    [InlineData("/api/hubs/sync/negotiate")]
    public async Task Preflight_FromAppOrigin_IsAllowedWithoutToken(string path)
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(Preflight(path, AppOrigin));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(AppOrigin, GetAllowOrigin(response));
    }

    [Fact]
    public async Task AuthenticatedRequest_FromAppOrigin_HasAllowOrigin()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.SendAsync(GetMe(AppOrigin));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(AppOrigin, GetAllowOrigin(response));
    }

    [Fact]
    public async Task Requests_FromForeignOrigin_HaveNoAllowOrigin()
    {
        await using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var preflight = await client.SendAsync(Preflight("/api/sync", ForeignOrigin));
        var me = await client.SendAsync(GetMe(ForeignOrigin));

        Assert.Null(GetAllowOrigin(preflight));
        Assert.Null(GetAllowOrigin(me));
    }

    [Fact]
    public async Task NoConfiguredOrigins_AppOriginIsNotAllowed()
    {
        await using var factory = new ApiFactory();
        using var client = factory.CreateClient();

        var preflight = await client.SendAsync(Preflight("/api/sync", AppOrigin));
        var me = await client.SendAsync(GetMe(AppOrigin));

        Assert.Null(GetAllowOrigin(preflight));
        Assert.Null(GetAllowOrigin(me));
    }

    private static HttpRequestMessage Preflight(string path, string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Options, path);
        request.Headers.Add("Origin", origin);
        request.Headers.Add("Access-Control-Request-Method", "POST");
        request.Headers.Add("Access-Control-Request-Headers", "authorization,content-type");
        return request;
    }

    private static HttpRequestMessage GetMe(string origin)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/me");
        request.Headers.Add("Origin", origin);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokens.Create($"cors-{Guid.NewGuid():N}", ApiFactory.AllowedEmail));
        return request;
    }

    private static string? GetAllowOrigin(HttpResponseMessage response) =>
        response.Headers.TryGetValues(AllowOriginHeader, out var values) ? values.Single() : null;
}
