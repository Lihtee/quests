using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Quests.Api.Features.Sync;
using Quests.Api.Features.Tasks;

namespace Quests.Tests.Features.Sync;

/// <summary><c>POST /api/sync</c>: per-field merge, tombstones, preserve-beats-delete, validation and isolation.</summary>
public sealed class SyncApiTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private const string SyncPath = "/api/sync";
    private const string Title = TaskFields.TitleField;
    private const string Notes = TaskFields.NotesField;
    private const string CompletedAt = TaskFields.CompletedAtField;

    private static readonly DateTimeOffset DeviceTime = new(2026, 11, 9, 8, 0, 0, TimeSpan.FromHours(3));

    [Fact]
    public async Task NoToken_Returns401()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsJsonAsync(SyncPath, new SyncRequest(0, []));

        await AssertStatusAsync(response, HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MalformedBody_Returns400()
    {
        using var client = CreateClient(factory, NewToken());

        var invalidJson = await client.PostAsync(SyncPath, new StringContent("{ not json", null, "application/json"));
        var negativeSince = await client.PostAsJsonAsync(SyncPath, new SyncRequest(-1, []));

        await AssertStatusAsync(invalidJson, HttpStatusCode.BadRequest);
        await AssertStatusAsync(negativeSince, HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Upsert_UnknownTask_CreatesWithClientIdAndCreatedAt()
    {
        using var client = CreateClient(factory, NewToken());
        var id = Guid.NewGuid();

        var response = await SyncAsync(client, 0,
            Upsert(id, 0, [Title, Notes], title: "  Write report ", notes: "draft", createdAt: DeviceTime));

        Assert.Empty(response.Rejected);
        var task = Assert.Single(response.Tasks);
        Assert.Equal(id, task.Id);
        Assert.Equal("Write report", task.Title);
        Assert.Equal("draft", task.Notes);
        Assert.Equal(DeviceTime.UtcDateTime, task.CreatedAt.UtcDateTime);
        Assert.Null(task.DeletedAt);
        Assert.Equal(response.Version, task.Version);
    }

    [Fact]
    public async Task TwoDevices_ChangingDifferentFields_BothKept()
    {
        var token = NewToken();
        using var deviceA = CreateClient(factory, token);
        using var deviceB = CreateClient(factory, token);
        var id = Guid.NewGuid();
        var created = await SyncAsync(deviceA, 0, Upsert(id, 0, [Title, Notes], title: "Old", notes: "notes"));
        var baseVersion = Single(created, id).Version;
        var doneAt = DeviceTime.AddHours(1);

        await SyncAsync(deviceB, created.Version, Upsert(id, baseVersion, [CompletedAt], completedAt: doneAt));
        var merged = await SyncAsync(deviceA, created.Version, Upsert(id, baseVersion, [Title], title: "New"));

        var task = Single(merged, id);
        Assert.Equal("New", task.Title);
        Assert.Equal("notes", task.Notes);
        Assert.Equal(doneAt.UtcDateTime, task.CompletedAt!.Value.UtcDateTime);
    }

    [Fact]
    public async Task SameField_LastArrivalWins()
    {
        using var client = CreateClient(factory, NewToken());
        var id = Guid.NewGuid();
        await SyncAsync(client, 0, Upsert(id, 0, [Title], title: "First"));

        var response = await SyncAsync(client, 0,
            Upsert(id, 1, [Title], title: "Device A"), Upsert(id, 1, [Title], title: "Device B"));

        Assert.Equal("Device B", Single(response, id).Title);
    }

    [Fact]
    public async Task Delete_WhenDeviceSawLatest_CreatesTombstone()
    {
        using var client = CreateClient(factory, NewToken());
        var id = Guid.NewGuid();
        var created = await SyncAsync(client, 0, Upsert(id, 0, [Title], title: "Task"));

        var deleted = await SyncAsync(client, created.Version, Delete(id, Single(created, id).Version));

        var tombstone = Single(deleted, id);
        Assert.NotNull(tombstone.DeletedAt);
        Assert.Equal("Task", tombstone.Title);
        Assert.Empty((await SyncAsync(client, 0)).Tasks);
    }

    [Fact]
    public async Task Delete_WhenTaskChangedElsewhere_IsIgnoredAndTaskReturned()
    {
        var token = NewToken();
        using var deviceA = CreateClient(factory, token);
        using var deviceB = CreateClient(factory, token);
        var id = Guid.NewGuid();
        var created = await SyncAsync(deviceA, 0, Upsert(id, 0, [Title], title: "Task"));
        var seenVersion = Single(created, id).Version;
        var edited = await SyncAsync(deviceB, created.Version, Upsert(id, seenVersion, [Notes], notes: "edited"));

        // Device A pulls from a version after the edit, so the task comes back only because the delete was ignored.
        var response = await SyncAsync(deviceA, edited.Version, Delete(id, seenVersion));

        var task = Single(response, id);
        Assert.Null(task.DeletedAt);
        Assert.Equal("edited", task.Notes);
    }

    [Fact]
    public async Task Upsert_OnTombstone_RestoresWithEditApplied()
    {
        using var client = CreateClient(factory, NewToken());
        var id = Guid.NewGuid();
        var created = await SyncAsync(client, 0, Upsert(id, 0, [Title, Notes], title: "Task", notes: "notes"));
        var deleted = await SyncAsync(client, created.Version, Delete(id, Single(created, id).Version));

        var restored = await SyncAsync(client, deleted.Version, Upsert(id, Single(created, id).Version, [Title], title: "Back"));

        var task = Single(restored, id);
        Assert.Null(task.DeletedAt);
        Assert.Equal("Back", task.Title);
        Assert.Equal("notes", task.Notes);
    }

    [Fact]
    public async Task Delete_UnknownOrAlreadyDeleted_IsNoOp()
    {
        using var client = CreateClient(factory, NewToken());
        var id = Guid.NewGuid();
        var created = await SyncAsync(client, 0, Upsert(id, 0, [Title], title: "Task"));
        var deleted = await SyncAsync(client, created.Version, Delete(id, Single(created, id).Version));
        var deletedAt = Single(deleted, id).DeletedAt;

        var response = await SyncAsync(client, 0, Delete(Guid.NewGuid(), 0), Delete(id, long.MaxValue));

        Assert.Empty(response.Rejected);
        Assert.Empty(response.Tasks);
        var again = await SyncAsync(client, created.Version);
        Assert.Equal(deletedAt, Single(again, id).DeletedAt);
    }

    public static TheoryData<string> InvalidChangeCases =>
        ["blank-title", "long-title", "long-notes", "unknown-field", "unknown-op", "no-fields"];

    [Theory]
    [MemberData(nameof(InvalidChangeCases))]
    public async Task InvalidChange_IsRejected_NothingSaved_CurrentStateReturned(string invalidCase)
    {
        using var client = CreateClient(factory, NewToken());
        var id = Guid.NewGuid();
        var created = await SyncAsync(client, 0, Upsert(id, 0, [Title], title: "Original"));
        var change = invalidCase switch
        {
            "blank-title" => Upsert(id, 1, [Title, Notes], title: "   ", notes: "n"),
            "long-title" => Upsert(id, 1, [Title], title: new string('a', TaskItem.TitleMaxLength + 1)),
            "long-notes" => Upsert(id, 1, [Notes], notes: new string('n', TaskItem.NotesMaxLength + 1)),
            "unknown-field" => Upsert(id, 1, [Notes, "ownerId"], notes: "n"),
            "unknown-op" => Upsert(id, 1, [Notes], notes: "n") with { Op = "patch" },
            "no-fields" => Upsert(id, 1, []),
            _ => throw new ArgumentOutOfRangeException(nameof(invalidCase), invalidCase, null),
        };

        var response = await SyncAsync(client, created.Version, change);

        var rejected = Assert.Single(response.Rejected);
        Assert.Equal(new RejectedChange(id, RejectedChange.Invalid), rejected);
        var task = Single(response, id);
        Assert.Equal("Original", task.Title);
        Assert.Null(task.Notes);
        Assert.Equal(Single(created, id).Version, task.Version);
    }

    [Fact]
    public async Task NewTaskWithoutTitle_IsRejected()
    {
        using var client = CreateClient(factory, NewToken());
        var id = Guid.NewGuid();

        var response = await SyncAsync(client, 0, Upsert(id, 0, [Notes], notes: "n"));

        Assert.Equal(new RejectedChange(id, RejectedChange.Invalid), Assert.Single(response.Rejected));
        Assert.Empty(response.Tasks);
        Assert.Empty((await SyncAsync(client, 0)).Tasks);
    }

    [Fact]
    public async Task Pull_ReturnsOnlyNewerChanges_IncludingTombstones()
    {
        using var client = CreateClient(factory, NewToken());
        var kept = Guid.NewGuid();
        var removed = Guid.NewGuid();
        var created = await SyncAsync(client, 0,
            Upsert(kept, 0, [Title], title: "Kept"), Upsert(removed, 0, [Title], title: "Removed"));
        Assert.Equal(2, created.Tasks.Length);

        var deleted = await SyncAsync(client, created.Version, Delete(removed, Single(created, removed).Version));

        Assert.Empty((await SyncAsync(client, deleted.Version)).Tasks);
        var sinceCreate = await SyncAsync(client, created.Version);
        Assert.NotNull(Single(sinceCreate, removed).DeletedAt);
        Assert.DoesNotContain(sinceCreate.Tasks, t => t.Id == kept);
        var firstPull = await SyncAsync(client, 0);
        Assert.Equal(kept, Assert.Single(firstPull.Tasks).Id);
        Assert.Equal(deleted.Version, firstPull.Version);
    }

    [Fact]
    public async Task OtherUser_ChangesAreRejected_DataUnchangedAndNotReturned()
    {
        const string otherEmail = "other@test.local";
        await using var twoUsers = ApiFactory.WithSettings(new Dictionary<string, string>
        {
            ["ALLOWED_EMAILS"] = $"{ApiFactory.AllowedEmail},{otherEmail}",
        });
        using var owner = CreateClient(twoUsers, NewToken());
        using var other = CreateClient(twoUsers, NewToken(otherEmail));
        var id = Guid.NewGuid();
        var created = await SyncAsync(owner, 0, Upsert(id, 0, [Title], title: "private"));

        var response = await SyncAsync(other, 0,
            Upsert(id, long.MaxValue, [Title], title: "hacked"), Delete(id, long.MaxValue));

        Assert.All(response.Rejected, r => Assert.Equal(new RejectedChange(id, RejectedChange.Forbidden), r));
        Assert.Equal(2, response.Rejected.Length);
        Assert.Empty(response.Tasks);
        var ownerView = await SyncAsync(owner, 0);
        var task = Single(ownerView, id);
        Assert.Equal("private", task.Title);
        Assert.Null(task.DeletedAt);
        Assert.Equal(Single(created, id).Version, task.Version);
    }

    private static TaskChange Upsert(
        Guid id, long baseVersion, string[] changed, string? title = null, string? notes = null,
        DateTimeOffset? completedAt = null, DateTimeOffset? createdAt = null) =>
        new(id, TaskChange.UpsertOp, baseVersion, changed, title, notes, null, completedAt, createdAt);

    private static TaskChange Delete(Guid id, long baseVersion) =>
        new(id, TaskChange.DeleteOp, baseVersion, null, null, null, null, null, null);

    private static TaskDto Single(SyncResponse response, Guid id) => Assert.Single(response.Tasks, t => t.Id == id);

    /// <summary>A token for a new user (fresh <c>sub</c>). Clients sharing a token act as devices of one user.</summary>
    private static string NewToken(string email = ApiFactory.AllowedEmail) =>
        TestTokens.Create($"uid-{Guid.NewGuid():N}", email);

    private static HttpClient CreateClient(ApiFactory apiFactory, string token)
    {
        var client = apiFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }

    private static async Task<SyncResponse> SyncAsync(HttpClient client, long since, params TaskChange[] changes)
    {
        var response = await client.PostAsJsonAsync(SyncPath, new SyncRequest(since, changes));
        await AssertStatusAsync(response, HttpStatusCode.OK);
        return (await response.Content.ReadFromJsonAsync<SyncResponse>())!;
    }

    private static async Task AssertStatusAsync(HttpResponseMessage response, HttpStatusCode expected)
    {
        if (response.StatusCode == expected)
            return;

        var body = await response.Content.ReadAsStringAsync();
        Assert.Fail($"Expected {(int)expected}, got {(int)response.StatusCode}. Body: {body}");
    }
}
