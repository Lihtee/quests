using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using Quests.Api.Features.Sync;
using Quests.Api.Features.Tasks;

namespace Quests.Tests.Features.Sync;

/// <summary>
/// <see cref="SyncHub"/> doorbell: hub auth, and which connections get "changed" after <c>POST /api/sync</c>.
/// Connections use WebSockets with the token in the <c>access_token</c> query, like a browser.
/// </summary>
public sealed class SyncHubTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly TimeSpan SignalTimeout = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan NoSignalWait = TimeSpan.FromMilliseconds(500);

    [Fact]
    public async Task Negotiate_NoToken_Returns401()
    {
        using var client = factory.CreateClient();

        var response = await client.PostAsync($"{SyncHub.Path}/negotiate?negotiateVersion=1", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Negotiate_NotAllowlisted_Returns403()
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", NewToken("stranger@test.local"));

        var response = await client.PostAsync($"{SyncHub.Path}/negotiate?negotiateVersion=1", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AppliedChange_WithSenderHeader_SignalsOtherConnectionsOnly()
    {
        var token = NewToken();
        await using var a = await ConnectAsync(token);
        await using var b = await ConnectAsync(token);

        await SyncAsync(token, a.Connection.ConnectionId, NewTask());

        await b.WaitForSignalAsync();
        await a.AssertNoSignalAsync();
    }

    [Fact]
    public async Task AppliedChange_WithoutSenderHeader_SignalsAllConnections()
    {
        var token = NewToken();
        await using var a = await ConnectAsync(token);
        await using var b = await ConnectAsync(token);

        await SyncAsync(token, null, NewTask());

        await a.WaitForSignalAsync();
        await b.WaitForSignalAsync();
    }

    [Fact]
    public async Task NothingApplied_SendsNoSignal()
    {
        var token = NewToken();
        await using var a = await ConnectAsync(token);
        await using var b = await ConnectAsync(token);

        await SyncAsync(token, a.Connection.ConnectionId);

        await b.AssertNoSignalAsync();
        await a.AssertNoSignalAsync();
    }

    [Fact]
    public async Task AppliedChange_DoesNotSignalOtherUsers()
    {
        var token = NewToken();
        await using var own = await ConnectAsync(token);
        await using var other = await ConnectAsync(NewToken());

        await SyncAsync(token, null, NewTask());

        await own.WaitForSignalAsync();
        await other.AssertNoSignalAsync();
    }

    private static string NewToken(string email = ApiFactory.AllowedEmail) =>
        TestTokens.Create($"uid-{Guid.NewGuid():N}", email);

    private static TaskChange NewTask() =>
        new(Guid.NewGuid(), TaskChange.UpsertOp, 0, [TaskFields.TitleField], "Signal me", null, null, null, null);

    private async Task SyncAsync(string token, string? senderConnectionId, params TaskChange[] changes)
    {
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (senderConnectionId is not null)
            client.DefaultRequestHeaders.Add(SyncEndpoints.ConnectionIdHeader, senderConnectionId);

        var response = await client.PostAsJsonAsync("/api/sync", new SyncRequest(0, changes));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private async Task<Listener> ConnectAsync(string token)
    {
        var server = factory.Server;
        var connection = new HubConnectionBuilder()
            .WithUrl(new Uri(server.BaseAddress, SyncHub.Path.TrimStart('/')), options =>
            {
                options.Transports = HttpTransportType.WebSockets;
                options.HttpMessageHandlerFactory = _ => server.CreateHandler();
                options.AccessTokenProvider = () => Task.FromResult<string?>(token);
                options.WebSocketFactory = async (context, ct) =>
                {
                    // Like a browser: the token goes in the query, not in a header.
                    var uri = new UriBuilder(context.Uri);
                    if (!uri.Query.Contains("access_token=", StringComparison.Ordinal))
                        uri.Query = $"{uri.Query.TrimStart('?')}&access_token={Uri.EscapeDataString(token)}";
                    return await server.CreateWebSocketClient().ConnectAsync(uri.Uri, ct);
                };
            })
            .Build();

        var listener = new Listener(connection);
        await connection.StartAsync();
        return listener;
    }

    private sealed class Listener : IAsyncDisposable
    {
        private readonly TaskCompletionSource _signal = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Listener(HubConnection connection)
        {
            Connection = connection;
            connection.On(SyncHub.ChangedMethod, () => _signal.TrySetResult());
        }

        public HubConnection Connection { get; }

        public Task WaitForSignalAsync() => _signal.Task.WaitAsync(SignalTimeout);

        public async Task AssertNoSignalAsync()
        {
            await Task.WhenAny(_signal.Task, Task.Delay(NoSignalWait));
            Assert.False(_signal.Task.IsCompleted, "Unexpected 'changed' signal.");
        }

        public ValueTask DisposeAsync() => Connection.DisposeAsync();
    }
}
