# Implementation Plan: Offline-first sync

Branch: none (git.create_branches=false)
Created: 2026-11-09

## Original Request
offline-first sync

## Settings
- Testing: yes (backend xUnit + frontend Vitest with fake-indexeddb)
- Logging: verbose
- Docs: yes
- Commit strategy: incremental
- Development methodology: implementation-first

## Roadmap Linkage
Milestone: "Offline-first sync"
Rationale: The user named this roadmap milestone as the planning request.

## Decisions (confirmed by user)
- **Conflicts — offline changes are never discarded.** Each change carries only the fields it changed. The server applies them per field, in arrival order, so the device that syncs last wins for the fields it changed; other fields are kept. (User: "pick most respectful to offline changes".)
- **Preserve beats delete, always.** A delete is applied only if the deleting device had already seen the latest server version of the task. If the task changed on the server since then, the delete is ignored and the task comes back on that device. An edit to a deleted task restores it (with the edit applied).
- **Remove the REST CRUD endpoints** (`/api/tasks`). `POST /api/sync` is the only write path.
- **No service worker for now.** Offline works while the tab stays open; reloading the web page offline is out of scope (the Android milestone covers offline start).
- **No sync status UI.** Sync runs silently; sync errors are logged to the console.

## Defaults (not asked; change during implementation if wanted)
- Deletes are soft on the server: `TaskItem.DeletedAt` (tombstone, row and fields kept, so a restore keeps the old data). Tombstones are kept forever (per-user lists are small).
- Versioning: `User.SyncVersion` is a per-user counter. Every applied change sets `task.Version = ++SyncVersion`. Pull returns tasks with `Version > since`. With `since = 0`, tombstones are skipped.
- Clients generate task ids (`crypto.randomUUID()`) and send `createdAt` / `completedAt` from the device clock (user data). The server sets `UpdatedAt`.
- One Dexie database per signed-in user (`quests-<uid>`). Sign-out keeps it, so pending changes sync on that user's next sign-in.
- Sync triggers: app start, window focus, `online` event, every 60 s while the page is visible, and ~500 ms after each local write. One sync at a time; a trigger during a running sync queues one rerun. On failure the outbox is kept and the next trigger retries.
- Offline start (tab already loaded, or later in Android): `AuthGate` caches the last good `/api/me` result in the user's Dexie `meta` table and uses it when `/api/me` fails with a network error. 403 still shows "not invited".
- Sort order moves to the client (`frontend/src/domain/`) with the same rules as today's server `Sort`.
- Logging (frontend): sync errors → `console.error` in all builds (no task content). Flow details → `console.debug` in DEV only.

## Requirements Reconciliation
Authority: none declared. `ROADMAP.md` (Offline-first sync) sets the scope. `DESCRIPTION.md` / `ARCHITECTURE.md` refine the design. User answers in this session decide the open points.

| Source | Rule | Relation | Resolution |
|---|---|---|---|
| DESCRIPTION — Open Decisions "Conflict resolution details" | to be decided | Decided by user | Per-field, arrival order (offline changes always applied) |
| ARCHITECTURE — "all writes go to Dexie + outbox"; "UI always reads from Dexie" | Write/read path | Agrees with ROADMAP | UI uses Dexie only; the sync engine is the only API caller for tasks |
| DESCRIPTION — "timer" sync trigger; NFR Battery | Timer while open | Refines | 60 s interval only while `document.visibilityState === 'visible'` |
| rules/base.md — "frontend console logging in development only" | DEV-only logs | Conflicts with user "log errors" | User answer is newer and specific: sync **errors** log in all builds; everything else DEV-only |
| rules/base.md — "sync failures never lose local changes" | Keep queue | Agrees | Outbox entries are removed only after a successful sync response |
| ARCHITECTURE — Key Principle 3 (owner scope) | Owner filter | Agrees | Pushing a change for another user's task id is rejected (existence check by id with `IgnoreQueryFilters()` — the one documented exception) |
| ROADMAP — Real-time updates (SignalR) | next milestone | Out of scope | Not in this plan |

| Combination (change × server state) | Result | Verification |
|---|---|---|
| upsert × task unknown | Create (title required, validated) | SyncApiTests |
| upsert × task exists | Apply listed fields only; `Version` bumped | SyncApiTests (two-device field merge) |
| upsert × task is tombstone | Restore (`DeletedAt = null`) + apply fields | SyncApiTests |
| upsert × invalid fields (blank/long title, long notes, unknown field) | Rejected, nothing saved; task's current state returned | SyncApiTests |
| delete × `task.Version <= baseVersion` | Tombstone; `Version` bumped | SyncApiTests |
| delete × `task.Version > baseVersion` (changed elsewhere) | Ignored; task returned in the response | SyncApiTests + Vitest (task reappears locally) |
| delete × task unknown / already tombstone | No-op | SyncApiTests |
| any change × task id owned by another user | Rejected; other user's data unchanged and not returned | SyncApiTests (isolation) |
| no token | 401 | SyncApiTests |
| pull echo × local edit made during the request | Local pending fields kept over pulled values | Vitest |
| network failure × outbox | Outbox kept, local data unchanged | Vitest |

Meaning preservation: PASS

## Tasks

### Phase 1: Backend sync API

- [x] Task 1: Sync fields on `TaskItem` and `User`, migration
  - Files: `backend/Quests.Api/Features/Tasks/TaskItem.cs`, `backend/Quests.Api/Features/Tasks/TaskItemConfiguration.cs`, `backend/Quests.Api/Features/Identity/User.cs`, `backend/Quests.Api/Features/Identity/UserConfiguration.cs`, `backend/Quests.Api/Infrastructure/Data/Migrations/<timestamp>_AddSync.cs` (+ Designer, snapshot)
  - `TaskItem`: add `long Version`, `DateTimeOffset? DeletedAt`, `bool IsDeleted`. `Create` takes an explicit `Guid id` and `createdAt` (client-generated). Replace `Update/Complete/Reopen` with `Apply(TaskFields fields, long version, DateTimeOffset now)` where `TaskFields` is a small record of optional values + the set of changed field names (title, notes, dueAt, completedAt); also `MarkDeleted(long version, DateTimeOffset now)` and restore inside `Apply` when deleted. Keep normalization and limits (`TitleMaxLength`, `NotesMaxLength`). No clock or EF in the model.
  - `User`: add `long SyncVersion`.
  - Configuration: index `(OwnerId, Version)` on Tasks.
  - Migration `AddSync` via `dotnet ef` (command as in README). In the migration, set existing tasks `Version = 1` and users `SyncVersion = 1`.
  - Logging: none in the model.

- [x] Task 2: Sync feature (`POST /api/sync`), remove CRUD endpoints (depends on 1)
  - Files: new `backend/Quests.Api/Features/Sync/SyncDtos.cs`, `SyncService.cs`, `SyncEndpoints.cs`; delete `backend/Quests.Api/Features/Tasks/TaskEndpoints.cs`, `TaskService.cs`, `TaskDtos.cs` (move field validation into `SyncDtos` or a `TaskValidation` helper in `Features/Tasks/`); `backend/Quests.Api/Program.cs`
  - Request: `SyncRequest(long Since, TaskChange[] Changes)`; `TaskChange(Guid Id, string Op /* "upsert" | "delete" */, long BaseVersion, string[] Changed, string? Title, string? Notes, DateTimeOffset? DueAt, DateTimeOffset? CompletedAt, DateTimeOffset? CreatedAt)`.
  - Response: `SyncResponse(long Version, TaskDto[] Tasks, RejectedChange[] Rejected)`; `TaskDto` includes `Version` and `DeletedAt`. `RejectedChange(Guid Id, string Reason)` — reason is a code (`invalid`, `forbidden`), never content.
  - `SyncService.SyncAsync`: one transaction. First reserve versions atomically (`ExecuteUpdateAsync` `SyncVersion += changes.Length` on the current user — takes the SQLite write lock), then apply changes in order following the combination table. Response tasks = `Version > Since` (skip tombstones when `Since == 0`) ∪ tasks touched by an ignored delete or rejected change (own tasks only). `Version` in the response = the user's `SyncVersion` after the commit. Sort is not needed on the server.
  - Endpoint: `MapPost("/api/sync")` under the existing fallback auth policy; malformed body → 400 ProblemDetails. Thin handler.
  - Register `SyncService`; remove `MapTaskEndpoints`/`TaskService` from `Program.cs`.
  - Logging (verbose): Information "Sync for {UserId}: {Applied} applied, {Ignored} ignored, {Rejected} rejected, {Returned} returned, version {Version}"; Debug per change "Change {Op} {TaskId}: {Outcome}"; Warning for `forbidden` (id owned by another user) with ids only. Never log title/notes.

- [x] Task 3: Backend tests (depends on 2)
  - Files: `backend/Quests.Tests/Features/Tasks/TaskItemTests.cs` (update), `backend/Quests.Tests/Features/Sync/SyncApiTests.cs` (new), delete `backend/Quests.Tests/Features/Tasks/TaskApiTests.cs`
  - `TaskItemTests`: `Apply` changes only listed fields and sets `Version`/`UpdatedAt`; restore from tombstone; `MarkDeleted`; normalization/limits kept.
  - `SyncApiTests` (`ApiFactory` + `TestTokens`): every row of the combination table, including two-device per-field merge (device A changes title, device B completes → both kept), stale delete ignored and returned, edit restores a deleted task, pull with `since` returns only newer changes and tombstones, isolation with two allowlisted users (`ApiFactory.WithSettings`), 401 without token.
  - Logging: assert messages report status code and body.
  - Verify: `dotnet test backend/Quests.slnx` passes.

### Phase 2: Frontend local store and sync engine

- [x] Task 4: Dexie, Vitest and pure task rules
  - Files: `frontend/package.json` (dependency `dexie`; devDependencies `vitest`, `fake-indexeddb`; script `"test": "vitest run"`), `frontend/vite.config.ts` (Vitest `test` block, environment `node`, setup file importing `fake-indexeddb/auto`), `frontend/src/shared/db.ts`, `frontend/src/domain/taskRules.ts`
  - `db.ts`: `openUserDb(uid)` returns a Dexie instance named `quests-<uid>` with tables `tasks` (`id`, fields as `TaskDto` + `deletedAt`), `outbox` (`++seq`, `taskId`; entry = `{ taskId, op, changed: string[], baseVersion, at }`), `meta` (key/value: `lastVersion`, `me`). Export a `Task` type.
  - `domain/taskRules.ts` (no React/Dexie/fetch): `sortTasks` (port of the server `Sort`), `validateTask({ title, notes })` → field errors (title required/≤200 after trim, notes ≤4000), `normalizeTask` (trim title, empty notes → null).
  - Logging: n/a (pure code / setup).

- [x] Task 5: Local writes and sync engine (depends on 2, 4)
  - Files: `frontend/src/features/tasks/taskStore.ts` (new), `frontend/src/features/sync/syncEngine.ts` (new), `frontend/src/features/sync/useSync.ts` (new)
  - `taskStore.ts`: `createTask`, `updateTask`, `completeTask`, `reopenTask`, `deleteTask`. Each validates with `domain/`, writes the `tasks` row and an outbox entry in one Dexie transaction (`baseVersion` = the row's current `version`; delete sets local `deletedAt`), then calls `requestSync()`. No `fetch` here.
  - `syncEngine.ts`: `syncNow(db)` single-flight with one queued rerun. Snapshot outbox up to max `seq` → build `TaskChange[]` from current local rows → `apiSend('POST', '/api/sync', …)`. On success, in one transaction: apply returned tasks (a returned tombstone deletes the local row; a pending outbox entry added after the snapshot keeps its local fields over the pulled values), delete pushed outbox entries (`seq <= snapshotMax`), store `lastVersion`. Rejected changes: drop their entries (the server state was returned). On failure: change nothing.
  - `useSync.ts`: start/stop triggers from Defaults (start, focus, `online`, 60 s visible-only interval, debounced `requestSync`). Cleans up listeners on unmount.
  - Logging: `console.error('[sync] failed', { status })` in all builds; DEV-only `console.debug('[sync] …', { pushed, pulled, rejected, version })`. No titles/notes.

- [x] Task 6: UI reads from Dexie; offline-capable sign-in screen (depends on 5)
  - Files: `frontend/src/features/tasks/TasksPage.tsx`, `frontend/src/features/tasks/TaskForm.tsx`, `frontend/src/features/tasks/TaskRow.tsx`, `frontend/src/features/auth/AuthGate.tsx`, delete `frontend/src/features/tasks/tasksApi.ts`
  - Provide the user's Dexie instance to the tasks page (prop or small context created in `SignedIn`, keyed by uid). `TasksPage` subscribes with Dexie `liveQuery` (no extra package), hides tombstones, sorts with `sortTasks`. No loading-from-server state; remove the refetch logic. Keep "Show completed", inline edit and inline delete confirm.
  - `TaskForm` shows `validateTask` errors instead of ProblemDetails.
  - `AuthGate`: cache `me` in `meta` after a successful `/api/me`; on a network error (not `ApiError`) use the cached value if present. Mount `useSync` in the signed-in state.
  - Logging: DEV-only `console.error('[tasks] … failed', error)` on failed local writes.
  - Verify: `npm run lint` and `npm run build` pass.

- [x] Task 7: Frontend tests (depends on 6)
  - Files: `frontend/src/domain/taskRules.test.ts`, `frontend/src/features/sync/syncEngine.test.ts`
  - `taskRules`: sort order, validation limits, normalization.
  - `syncEngine` (fake-indexeddb, mocked `fetch`/`apiSend`): local write → outbox → push body has only changed fields and `baseVersion`; success clears pushed entries and stores `lastVersion`; a pulled tombstone removes the local row; an ignored delete returns the task and it reappears; an edit made during the request survives the echo; network failure keeps the outbox and local data; rejected change entry is dropped.
  - Verify: `npm test` passes.
  - Manual / Playwright (Auth Emulator, as in `docs/local-testing.md`): two browser contexts, same account. Context A goes offline (`setOffline(true)`), creates/edits/completes/deletes tasks; reconnect → context B shows the changes after focus/refresh. Conflict check: A offline edits title, B completes the same task → both kept after A reconnects. Delete check: A deletes offline, B edits the task → after A reconnects the task is back on both. Second account still sees none of these tasks.

### Phase 3: Documentation

- [x] Task 8: Docs checkpoint (depends on 3, 7)
  - Files: `README.md`, `AGENTS.md`, `docs/local-testing.md`
  - Run `/aif-docs`: README — replace the tasks CRUD API section with `POST /api/sync` (request/response, conflict and delete rules), add `npm test`. AGENTS.md — add `Features/Sync/`, `frontend/src/features/sync/`, `frontend/src/domain/`, `shared/db.ts`; update the summary line. `docs/local-testing.md` — the offline Playwright scenario.
  - Logging: n/a.

## Commit Plan
- **Commit 1** (after tasks 1-3): "feat(sync): add delta sync API with per-field merge and tombstones"
- **Commit 2** (after tasks 4-7): "feat(sync): add offline-first Dexie store and sync engine"
- **Commit 3** (after task 8): "docs: document offline-first sync"
