using System.Net;
using System.Text.Json;

namespace Quests.Tests.Infrastructure;

/// <summary>Starts the app (which applies migrations) and checks the basic HTTP pipeline.</summary>
public sealed class SmokeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Health_ReturnsOk()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.OK,
            $"Expected 200, got {(int)response.StatusCode}. Body: {body}");
    }

    [Fact]
    public async Task UnknownRoute_ReturnsProblemDetails404WithTraceId()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/api/does-not-exist");
        var body = await response.Content.ReadAsStringAsync();

        Assert.True(response.StatusCode == HttpStatusCode.NotFound,
            $"Expected 404, got {(int)response.StatusCode}. Body: {body}");
        Assert.True(response.Content.Headers.ContentType?.MediaType == "application/problem+json",
            $"Expected application/problem+json, got '{response.Content.Headers.ContentType}'. Body: {body}");

        using var json = JsonDocument.Parse(body);
        Assert.True(json.RootElement.TryGetProperty("traceId", out _),
            $"Expected a traceId field. Body: {body}");
    }
}
