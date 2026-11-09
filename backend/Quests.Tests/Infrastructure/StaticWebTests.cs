using System.Net;
using System.Net.Http.Headers;

namespace Quests.Tests.Infrastructure;

/// <summary>Static web app + SPA fallback, and that unknown <c>/api/*</c> paths never return <c>index.html</c>.</summary>
public sealed class StaticWebTests(WebRootApiFactory factory) : IClassFixture<WebRootApiFactory>
{
    [Theory]
    [InlineData("/")]
    [InlineData("/some/client/route")]
    public async Task Page_ReturnsIndexHtmlWithNoCache(string path)
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"Expected 200, got {(int)response.StatusCode}. Body: {body}");
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(ApiFactory.IndexHtml, body);
        Assert.True(response.Headers.CacheControl?.NoCache == true,
            $"Expected Cache-Control: no-cache, got '{response.Headers.CacheControl}'");
    }

    [Fact]
    public async Task ExistingAsset_ReturnsFile()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/assets/app.js");
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"Expected 200, got {(int)response.StatusCode}. Body: {body}");
        Assert.Equal(ApiFactory.AppJs, body);
    }

    [Fact]
    public async Task Health_ReturnsOk()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UnknownApiPath_NoToken_Returns401ProblemDetails()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/does-not-exist");

        await AssertProblemAsync(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UnknownApiPath_WithToken_Returns404ProblemDetails()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
            "Bearer", TestTokens.Create($"uid-{Guid.NewGuid():N}", ApiFactory.AllowedEmail));

        var response = await client.GetAsync("/api/does-not-exist");

        await AssertProblemAsync(response, HttpStatusCode.NotFound);
    }

    private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected,
            $"Expected {(int)expected}, got {(int)response.StatusCode}. Body: {body}");
        Assert.True(response.Content.Headers.ContentType?.MediaType == "application/problem+json",
            $"Expected application/problem+json, got '{response.Content.Headers.ContentType}'. Body: {body}");
        Assert.DoesNotContain(ApiFactory.IndexHtml, body);
    }
}
