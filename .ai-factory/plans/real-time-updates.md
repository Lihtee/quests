# Implementation Plan: Real-time updates

Branch: none (git.create_branches=false)
Created: 2026-11-09

## Original Request
real-time updates

## Settings
- Testing: yes (backend xUnit with the SignalR .NET client over TestServer + frontend Vitest)
- Logging: verbose
- Docs: yes
- Commit strategy: incremental
- Development methodology: implementation-first

## Roadmap Linkage
Milestone: "Real-time updates"
Rationale: The user named this roadmap milestone as the planning request.

## Decisions (confirmed by user)
- **The signal skips the sender.** The client sends its SignalR connection id in the `X-Sync-Connection-Id` header on `POST /api/sync`. The server signals all other connections of the same user. Missing header → all of the user's connections get the signal.
- **Connect only while the page is visible.** The client opens the hub connection when the page becomes visible and stops it when the page is hidden. After every (re)connect it runs a sync to catch missed signals.

## Defaults (not asked; change during implementation if wanted)
- Hub path: `/api/hubs/sync` (under `/api`, so the Vite proxy, Caddy and the `/api/{**path}` 404 fallback already route it; map it before `MapQuestsStaticWeb`).
- The hub is server→client only. One client method: `changed` with no arguments (signals carry no data).
- Hub auth: the same fallback policy as `/api/sync` (authenticated + allowlisted). Browsers cannot set headers on WebSockets, so JwtBearer reads the token from the `access_token` query parameter, **only** for paths under `/api/hubs`.
- Routing: in `OnConnectedAsync` the hub finds the internal `User.Id` by the token's `sub` (`User.FirebaseUid`; the user row already exists because provisioning runs on the negotiate request) and adds the connection to group `user:<id:N>`. Unknown user → abort the connection with a warning.
- Signal only when the sync applied at least one change (`Applied > 0`). Pull-only syncs and syncs with only ignored/rejected changes send nothing.
- Signal failures never fail the sync: the notifier catches exceptions and logs a warning (the next delta sync catches up anyway).
- Client: `@microsoft/signalr` with `withAutomaticReconnect()`. On `changed` → `syncNow(db)` (the engine already coalesces concurrent runs). The existing triggers (start, focus, online, 60 s timer, after local writes) stay as the fallback.
- New dependencies (required by the feature): npm `@microsoft/signalr`; NuGet `Microsoft.AspNetCore.SignalR.Client` in `Quests.Tests` only. Server SignalR is part of ASP.NET Core (no package).
- Token safety: `Microsoft.AspNetCore` logs stay at `Warning` in `appsettings.json`, so request URLs with `access_token` are not logged. New code never logs the query string or the token.
- Caddy `reverse_proxy` forwards WebSockets by default; `deploy/` needs no change.

## Requirements Reconciliation
Authority: none declared. `ROADMAP.md` (Real-time updates) sets the scope. `DESCRIPTION.md` / `ARCHITECTURE.md` refine the design. User answers in this session decide the open points.

| Source | Rule | Relation | Resolution |
|--------|------|----------|------------|
| ROADMAP.md › Real-time updates | "edit in one tab/device, the other updates within seconds without refresh" | scope | Manual two-tab check in Task 7 |
| DESCRIPTION.md › Architecture Notes | SignalR is a doorbell; clients then run delta sync; missed signals are harmless | agrees with ARCHITECTURE.md | Signal has no payload; sync after every (re)connect |
| ARCHITECTURE.md › Layer/Module Communication | "after a sync commit, `SyncHub` sends a 'changed' signal to the user's other connections" | refined by user | Exclude the sender via `X-Sync-Connection-Id` |
| DESCRIPTION.md › Battery; ARCHITECTURE.md › Anti-Patterns | no sockets kept alive in the background | refined by user | Connect only while the page is visible |
| rules/base.md › Logging | never log tokens | constraint | Query-token only under `/api/hubs`; no query logging |

Behavior combinations (server, per `POST /api/sync`):

| Applied changes | `X-Sync-Connection-Id` | Signal sent to | Verification |
|-----------------|------------------------|----------------|--------------|
| > 0 | valid id of a user's connection | all the user's connections except that one | test: B gets `changed`, A does not |
| > 0 | missing | all the user's connections | test: A and B get `changed` |
| 0 (pull-only / ignored / rejected) | any | nobody | test: no `changed` within a short wait |
| > 0 | any | never another user's connections | test: other user's connection gets nothing |

Meaning preservation: PASS

## Tasks

### Phase 1: Server doorbell

- [x] Task 1: Add `SyncHub` with query-token auth and per-user groups
  - Files: `backend/Quests.Api/Features/Sync/SyncHub.cs` (new), `backend/Quests.Api/Infrastructure/Auth/FirebaseJwtSetup.cs`, `backend/Quests.Api/Program.cs`
  - `SyncHub : Hub` (inject `AppDbContext`, `ILogger<SyncHub>`): constants `Path = "/api/hubs/sync"`, `ChangedMethod = "changed"`, `static string GroupFor(Guid userId) => $"user:{userId:N}"`. `OnConnectedAsync`: read `sub` (`IdentityClaims.Subject`), look up `User.Id` by `FirebaseUid` (projection, no tracking); found → `Groups.AddToGroupAsync`; not found → log and `Context.Abort()`.
  - `FirebaseJwtSetup`: add `OnMessageReceived` to the existing `JwtBearerEvents`: if the path starts with `/api/hubs` and the query has `access_token`, set `context.Token`.
  - `Program.cs`: `builder.Services.AddSignalR()`; `app.MapHub<SyncHub>(SyncHub.Path)` after `MapSyncEndpoints()`, before `MapQuestsStaticWeb()`.
  - Logging: Debug "Hub connection {ConnectionId} joined group for {UserId}"; Debug on disconnect (`OnDisconnectedAsync`, with exception type if any); Warning "Hub connection {ConnectionId} rejected: no user for subject" (never log the token or query string). Debug in `OnMessageReceived` only "Token taken from query for {RequestPath}".

- [x] Task 2: Ring the doorbell after a sync that applied changes (depends on Task 1)
  - Files: `backend/Quests.Api/Features/Sync/SyncNotifier.cs` (new), `backend/Quests.Api/Features/Sync/SyncService.cs`, `backend/Quests.Api/Features/Sync/SyncEndpoints.cs`, `backend/Quests.Api/Program.cs`
  - `SyncNotifier(IHubContext<SyncHub> hub, ILogger<SyncNotifier> logger)`, `NotifyChangedAsync(Guid userId, string? excludeConnectionId, CancellationToken ct)`: `Clients.GroupExcept(group, [id])` when an id is given, else `Clients.Group(group)`; `SendAsync(SyncHub.ChangedMethod)`. Catch exceptions (not cancellation) → Warning; never rethrow. Register as singleton in `Program.cs`.
  - `SyncService.SyncAsync(SyncRequest request, string? senderConnectionId, CancellationToken ct)`: after `SaveChangesAsync`, if the batch applied ≥ 1 change → call the notifier.
  - `SyncEndpoints`: read the `X-Sync-Connection-Id` header (trimmed, empty → null; define the header name as a constant) and pass it to the service. No other logic in the lambda.
  - Logging: Debug "Signalled {UserId} connections (excluding {ExcludeConnectionId})"; Debug "No signal for {UserId}: nothing applied"; Warning on send failure with exception.

- [x] Task 3: Backend tests for the hub and the signal (depends on Task 2)
  - Files: `backend/Quests.Tests/Quests.Tests.csproj` (add `Microsoft.AspNetCore.SignalR.Client`), `backend/Quests.Tests/Features/Sync/SyncHubTests.cs` (new)
  - Helper: build a `HubConnection` to `factory.Server.BaseAddress + "api/hubs/sync"` with `HttpMessageHandlerFactory = _ => factory.Server.CreateHandler()`, `AccessTokenProvider` from `TestTokens.Create(...)`, WebSockets transport via `WebSocketFactory` using `factory.Server.CreateWebSocketClient()` (exercises the `access_token` query path), `SkipNegotiation = false`. Wait for `changed` with a `TaskCompletionSource` + timeout; "no signal" asserts use a short wait (~500 ms).
  - Cases: negotiate without token → 401; not-allowlisted email → 403; the four rows of the combination table above.
  - Logging: none beyond test output.

<!-- Commit checkpoint: Tasks 1-3 -->

### Phase 2: Web client

- [x] Task 4: Send the connection id with each sync
  - Files: `frontend/src/shared/api.ts`, `frontend/src/features/sync/syncEngine.ts`
  - `apiSend` / `request`: optional `headers?: Record<string, string>` merged into the request headers (Authorization stays).
  - `syncEngine`: module-level `let senderConnectionId: string | null`; export `setSyncConnectionId(id: string | null)`. `syncNow` passes `{ 'X-Sync-Connection-Id': id }` when set.
  - Logging: DEV-only `console.debug('[sync] connection id', id)` when it changes.

- [x] Task 5: SignalR client that connects while visible (depends on Task 4)
  - Files: `frontend/package.json` (+ lock file; add `@microsoft/signalr`), `frontend/src/features/sync/syncSignal.ts` (new), `frontend/src/features/sync/useSync.ts`, `frontend/vite.config.ts`
  - `startSyncSignal(db: QuestsDb): () => void`: build a `HubConnection` to `/api/hubs/sync` with `accessTokenFactory` → `auth.currentUser?.getIdToken()` and `withAutomaticReconnect()`. `on('changed')` → `syncNow(db)`. After `start()` and in `onreconnected` → `setSyncConnectionId(connection.connectionId)` + `syncNow(db)`. `onreconnecting` / `onclose` → `setSyncConnectionId(null)`. Listen to `visibilitychange`: visible → start (if `Disconnected`), hidden → `stop()`. Also start on `online` if visible and disconnected. The returned function removes listeners, stops the connection and clears the id.
  - `useSync`: call `startSyncSignal(db)` in the existing effect; call its stop in the cleanup.
  - `vite.config.ts`: `'/api': { target: 'http://localhost:5080', ws: true }`.
  - Logging: DEV-only `console.debug('[signal] ...')` for start/stop/reconnecting/reconnected/changed; start failures → DEV-only `console.debug` (harmless; the timer sync still runs). No token in logs.

- [x] Task 6: Frontend tests (depends on Task 5)
  - Files: `frontend/src/features/sync/syncEngine.test.ts`, `frontend/src/features/sync/syncSignal.test.ts` (new)
  - Engine: with `setSyncConnectionId('abc')` the `/api/sync` request has `X-Sync-Connection-Id: abc`; after `setSyncConnectionId(null)` the header is absent.
  - Signal: `vi.mock('@microsoft/signalr')` with a fake builder/connection; stub `document.visibilityState` and event listeners (test env is `node`). Cases: `changed` → sync runs; hidden → `stop()`; visible again → `start()` + sync; cleanup stops and clears the id.
  - Logging: none.

<!-- Commit checkpoint: Tasks 4-6 -->

### Phase 3: Docs

- [x] Task 7: Documentation checkpoint via `/aif-docs` (depends on Tasks 1-6)
  - Files: `README.md` (sync section: hub path, `changed` signal, `X-Sync-Connection-Id`), `docs/local-testing.md` (two-tab check: edit in tab A, tab B updates within seconds; hide tab B, edit, show it → updates), `AGENTS.md` (Sync folders: `SyncHub`, `SyncNotifier`, `syncSignal`).
  - Logging: n/a.

<!-- Commit checkpoint: Task 7 -->

## Commit Plan
1. After Tasks 1-3: `feat(sync): add SignalR doorbell hub and signal after sync`
2. After Tasks 4-6: `feat(sync): connect web client to the sync doorbell`
3. After Task 7: `docs: document real-time updates`
