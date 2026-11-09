using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Quests.Api.Features.Tasks;

namespace Quests.Tests.Features.Tasks;

/// <summary>Owner-scoped CRUD on <c>/api/tasks</c>.</summary>
public sealed class TaskApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string TasksPath = "/api/tasks";

    [Fact]
    public async Task NoToken_Returns401()
    {
        using var client = factory.CreateClient();

        var response = await client.GetAsync(TasksPath);

        await AssertStatusAsync(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Crud_RoundTrip()
    {
        using var client = CreateClient(factory, ApiFactory.AllowedEmail);
        var due = new DateTimeOffset(2026, 12, 1, 10, 0, 0, TimeSpan.FromHours(2));

        var createResponse = await client.PostAsJsonAsync(TasksPath, new TaskRequest("  Write report ", "draft", due));
        var created = await ReadAsync<TaskResponse>(createResponse, HttpStatusCode.Created);
        Assert.Equal($"{TasksPath}/{created.Id}", createResponse.Headers.Location?.OriginalString);
        Assert.Equal("Write report", created.Title);
        Assert.Equal("draft", created.Notes);
        Assert.Equal(due.UtcDateTime, created.DueAt!.Value.UtcDateTime);
        Assert.Null(created.CompletedAt);

        var updated = await ReadAsync<TaskResponse>(
            await client.PutAsJsonAsync($"{TasksPath}/{created.Id}", new TaskRequest("Final report", null, null)),
            HttpStatusCode.OK);
        Assert.Equal("Final report", updated.Title);
        Assert.Null(updated.Notes);
        Assert.Null(updated.DueAt);

        var completed = await ReadAsync<TaskResponse>(
            await client.PostAsync($"{TasksPath}/{created.Id}/complete", null), HttpStatusCode.OK);
        Assert.NotNull(completed.CompletedAt);

        var reopened = await ReadAsync<TaskResponse>(
            await client.PostAsync($"{TasksPath}/{created.Id}/reopen", null), HttpStatusCode.OK);
        Assert.Null(reopened.CompletedAt);

        var list = await ReadAsync<TaskResponse[]>(await client.GetAsync(TasksPath), HttpStatusCode.OK);
        Assert.Equal(created.Id, Assert.Single(list).Id);

        await AssertStatusAsync(await client.DeleteAsync($"{TasksPath}/{created.Id}"), HttpStatusCode.NoContent);
        Assert.Empty(await ReadAsync<TaskResponse[]>(await client.GetAsync(TasksPath), HttpStatusCode.OK));
        await AssertStatusAsync(await client.DeleteAsync($"{TasksPath}/{created.Id}"), HttpStatusCode.NotFound);
    }

    public static TheoryData<string> InvalidRequestCases => ["blank-title", "long-title", "long-notes"];

    [Theory]
    [MemberData(nameof(InvalidRequestCases))]
    public async Task InvalidRequest_Returns400AndSavesNothing(string invalidCase)
    {
        using var client = CreateClient(factory, ApiFactory.AllowedEmail);
        var request = invalidCase switch
        {
            "blank-title" => new TaskRequest("   ", null, null),
            "long-title" => new TaskRequest(new string('a', TaskItem.TitleMaxLength + 1), null, null),
            "long-notes" => new TaskRequest("Title", new string('n', TaskItem.NotesMaxLength + 1), null),
            _ => throw new ArgumentOutOfRangeException(nameof(invalidCase), invalidCase, null),
        };

        var response = await client.PostAsJsonAsync(TasksPath, request);

        await AssertStatusAsync(response, HttpStatusCode.BadRequest);
        Assert.Empty(await ReadAsync<TaskResponse[]>(await client.GetAsync(TasksPath), HttpStatusCode.OK));
    }

    [Fact]
    public async Task List_SortsDatedFirst_EarlierDueFirst_CompletedLast()
    {
        using var client = CreateClient(factory, ApiFactory.AllowedEmail);
        var now = DateTimeOffset.UtcNow;

        var undated = await CreateAsync(client, "undated", null);
        var done = await CreateAsync(client, "done", now.AddHours(1));
        var later = await CreateAsync(client, "later", now.AddDays(2));
        var sooner = await CreateAsync(client, "sooner", now.AddDays(1));
        await AssertStatusAsync(await client.PostAsync($"{TasksPath}/{done.Id}/complete", null), HttpStatusCode.OK);

        var list = await ReadAsync<TaskResponse[]>(await client.GetAsync(TasksPath), HttpStatusCode.OK);

        Assert.Equal([sooner.Id, later.Id, undated.Id, done.Id], list.Select(t => t.Id));
    }

    [Fact]
    public async Task OtherUser_CannotSeeOrChangeTask()
    {
        const string otherEmail = "other@test.local";
        await using var twoUsers = ApiFactory.WithSettings(new Dictionary<string, string>
        {
            ["ALLOWED_EMAILS"] = $"{ApiFactory.AllowedEmail},{otherEmail}",
        });
        using var owner = CreateClient(twoUsers, ApiFactory.AllowedEmail);
        using var other = CreateClient(twoUsers, otherEmail);
        var task = await CreateAsync(owner, "private", null);
        var path = $"{TasksPath}/{task.Id}";

        await AssertStatusAsync(await other.PutAsJsonAsync(path, new TaskRequest("hacked", null, null)), HttpStatusCode.NotFound);
        await AssertStatusAsync(await other.PostAsync($"{path}/complete", null), HttpStatusCode.NotFound);
        await AssertStatusAsync(await other.PostAsync($"{path}/reopen", null), HttpStatusCode.NotFound);
        await AssertStatusAsync(await other.DeleteAsync(path), HttpStatusCode.NotFound);
        Assert.Empty(await ReadAsync<TaskResponse[]>(await other.GetAsync(TasksPath), HttpStatusCode.OK));

        var ownerList = await ReadAsync<TaskResponse[]>(await owner.GetAsync(TasksPath), HttpStatusCode.OK);
        var unchanged = Assert.Single(ownerList);
        Assert.Equal("private", unchanged.Title);
        Assert.Null(unchanged.CompletedAt);
    }

    /// <summary>Each client is a new user (fresh <c>sub</c>), so tests don't share task lists.</summary>
    private static HttpClient CreateClient(ApiFactory apiFactory, string email)
    {
        var client = apiFactory.CreateClient();
        var token = TestTokens.Create($"uid-{Guid.NewGuid():N}", email);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<TaskResponse> CreateAsync(HttpClient client, string title, DateTimeOffset? dueAt) =>
        await ReadAsync<TaskResponse>(
            await client.PostAsJsonAsync(TasksPath, new TaskRequest(title, null, dueAt)), HttpStatusCode.Created);

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response, HttpStatusCode expected)
    {
        await AssertStatusAsync(response, expected);
        return (await response.Content.ReadFromJsonAsync<T>())!;
    }

    private static async Task AssertStatusAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode == expected)
            return;

        var body = await response.Content.ReadAsStringAsync();
        Assert.Fail($"Expected {(int)expected}, got {(int)response.StatusCode}. Body: {body}");
    }
}
