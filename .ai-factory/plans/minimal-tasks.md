# Implementation Plan: Minimal tasks

Branch: none (git.create_branches=false)
Created: 2026-11-09

## Original Request
minimal tasks

## Settings
- Testing: yes (backend only — xUnit; no frontend test setup)
- Logging: verbose
- Docs: yes
- Commit strategy: incremental
- Development methodology: implementation-first

## Roadmap Linkage
Milestone: "Minimal tasks"
Rationale: The user named this roadmap milestone as the planning request.

## Decisions (confirmed by user)
- Fields: title (required), notes (optional), a single due date/time (**optional**). Tasks without a due date sort after dated tasks.
- Completed tasks are **hidden by default**; a "Show completed" switch reveals them; a completed task can be reopened.
- Delete is a **hard delete**. Tombstones/soft delete come with the "Offline-first sync" milestone.
- Styling: **Pico CSS** (classless, minimal look, built-in light/dark). Chosen from a side-by-side sample of Pico, Simple.css, Water.css and Mantine.
- Editing is **inline**: Edit turns the row into the same `TaskForm` used for creating, with Save/Cancel.
- Online-only: the UI calls the API directly (no Dexie/outbox yet).

## Defaults (not asked; change during implementation if wanted)
- Limits: title ≤ 200 chars (trimmed, non-empty), notes ≤ 4000 chars (empty → null).
- `DueAt` is stored as UTC. The browser uses `<input type="datetime-local">` and shows dates in local time.
- Sort order (server): open tasks first, then by `DueAt` ascending with no-due-date last, then `CreatedAt`. Completed tasks follow, newest completion first. Sorting happens in memory after the owner-filtered query, because EF Core SQLite cannot `ORDER BY` a `DateTimeOffset` column. Per-user lists are small.
- Delete asks for confirmation with `window.confirm`.
- Task titles and notes are never logged (rules/base.md: no personal task content at Information or above).

## Requirements Reconciliation
Authority: none declared. `ROADMAP.md` (Minimal tasks) sets the scope. `ARCHITECTURE.md` refines file placement and model/service split.

| Source | Rule | Relation | Resolution |
|---|---|---|---|
| ROADMAP — "online-only for now" | UI talks to the API | Conflicts with ARCHITECTURE — "all writes go to Dexie + outbox" | ROADMAP is more specific for this milestone. Dexie/outbox come with "Offline-first sync". |
| ROADMAP — "single due date/time" | One `DueAt` field | Refines ARCHITECTURE's `TimeWindow` example | `TimeWindow` comes with the "Time windows" milestone. |
| ARCHITECTURE — Key Principle 3 | Every query scoped by owner | Agrees with rules/base.md | `TaskItem : IOwnedEntity` uses the existing global filter. |

| Combination (caller × task) | Result | Verification |
|---|---|---|
| Owner × own task: create/get/update/complete/reopen/delete | 201/200/200/200/200/204 | TaskApiTests |
| Other allowlisted user × someone else's task: update/complete/reopen/delete | 404; the other user's list does not contain it | TaskApiTests (isolation) |
| Owner × invalid body (blank/too long title, too long notes) | 400 ValidationProblem, nothing saved | TaskApiTests |
| No token | 401 | TaskApiTests |

Meaning preservation: PASS

## Tasks

### Phase 1: Backend tasks API

- [x] Task 1: `TaskItem` model, EF configuration and migration
  - Files: `backend/Quests.Api/Features/Tasks/TaskItem.cs`, `backend/Quests.Api/Features/Tasks/TaskItemConfiguration.cs`, `backend/Quests.Api/Infrastructure/Data/AppDbContext.cs`, `backend/Quests.Api/Infrastructure/Data/Migrations/<timestamp>_AddTasks.cs` (+ Designer, snapshot)
  - `TaskItem : IOwnedEntity` (same style as `User`): `Guid Id` (`Guid.CreateVersion7(now)`), `Guid OwnerId`, `string Title`, `string? Notes`, `DateTimeOffset? DueAt`, `DateTimeOffset CreatedAt`, `DateTimeOffset UpdatedAt`, `DateTimeOffset? CompletedAt`; private EF constructor.
  - Methods take `now` as a parameter (no clock inside the model): `static Create(ownerId, title, notes, dueAt, now)`, `Update(title, notes, dueAt, now)`, `Complete(now)` (no-op if already completed), `Reopen(now)` (no-op if open). Normalize: trim title, empty notes → null, `DueAt` → UTC. Guard invariants with `ArgumentException`. Input validation for the API is in Task 2, so these guards are not expected control flow.
  - Expose constants `TitleMaxLength = 200` and `NotesMaxLength = 4000`.
  - Configuration: key `Id` (`ValueGeneratedNever`), required `Title` with max length, `Notes` max length, FK `OwnerId → User.Id` with cascade delete, index on `OwnerId`. Add `DbSet<TaskItem> Tasks` to `AppDbContext`. The owner filter is applied automatically.
  - Add migration `AddTasks` with `dotnet ef` (local tool, command as in README).
  - Logging: none in the model (pure domain).

- [x] Task 2: `TaskService`, DTOs, endpoints and wiring (depends on 1)
  - Files: `backend/Quests.Api/Features/Tasks/TaskDtos.cs`, `backend/Quests.Api/Features/Tasks/TaskService.cs`, `backend/Quests.Api/Features/Tasks/TaskEndpoints.cs`, `backend/Quests.Api/Program.cs`
  - DTOs: `TaskRequest(string? Title, string? Notes, DateTimeOffset? DueAt)` for create and update (PUT is a full replace). `TaskResponse(Id, Title, Notes, DueAt, CompletedAt, CreatedAt, UpdatedAt)` with `From(TaskItem)`. A static `Validate(TaskRequest)` returns `Dictionary<string, string[]>` errors (title required/≤200 after trim, notes ≤4000).
  - `TaskService(AppDbContext db, CurrentUser currentUser, TimeProvider clock, ILogger<TaskService> logger)`: `ListAsync` (owner-filtered query, then in-memory sort from Defaults), `CreateAsync`, `UpdateAsync`, `CompleteAsync`, `ReopenAsync`, `DeleteAsync`. Lookups use `db.Tasks.FirstOrDefaultAsync(t => t.Id == id)` (filter applies). Missing → `null`/`false`. Missing `currentUser.Id` → `InvalidOperationException`, same as `MeEndpoints`.
  - Endpoints in `MapGroup("/api/tasks")`, protected by the existing fallback policy:
    - `GET /` → 200 `TaskResponse[]` (all tasks, sorted; the client filters completed)
    - `POST /` → 201 + `Location`, or 400 `ValidationProblem`
    - `PUT /{id:guid}` → 200 / 400 / 404
    - `POST /{id:guid}/complete`, `POST /{id:guid}/reopen` → 200 `TaskResponse` / 404
    - `DELETE /{id:guid}` → 204 / 404
  - Handlers stay thin: validate → service → result. 404 uses ProblemDetails (`TypedResults.Problem(statusCode: 404)` or the existing ProblemDetails setup).
  - Register `builder.Services.AddScoped<TaskService>()` and `app.MapTaskEndpoints()` in `Program.cs` before `MapQuestsStaticWeb()`.
  - Logging (verbose): Debug "Listing tasks for {UserId}: {Count}"; Information "Task {TaskId} created/updated/completed/reopened/deleted for {UserId}" (ids only); Debug "Task {TaskId} not found for {UserId}"; Debug on validation failure with the failing field names only. Never log title/notes.

- [x] Task 3: Backend tests (depends on 2)
  - Files: `backend/Quests.Tests/Features/Tasks/TaskItemTests.cs`, `backend/Quests.Tests/Features/Tasks/TaskApiTests.cs`
  - `TaskItemTests` (pure): Create trims/normalizes and sets timestamps from `now`; Update changes fields and `UpdatedAt`; Complete/Reopen set/clear `CompletedAt` and are idempotent; `DueAt` is converted to UTC.
  - `TaskApiTests` (use `ApiFactory` + `TestTokens`, same as `AuthTests`): full CRUD round-trip; 400 for blank title / title > 200 / notes > 4000; sort order (dated before undated, earlier due first, completed last); 401 without token; isolation — with `ApiFactory.WithSettings` allowlisting two emails, user B gets 404 on A's task for PUT/complete/reopen/delete and B's list does not contain it.
  - Logging: no test-specific logging; failures must report status code and body (assert messages).
  - Verify: `dotnet test backend/Quests.slnx` passes.

### Phase 2: Web UI

- [x] Task 4: Pico CSS and API write helper
  - Files: `frontend/package.json`, `frontend/src/main.tsx`, `frontend/src/index.css`, `frontend/src/App.tsx`, `frontend/src/shared/api.ts`
  - Add dependency `@picocss/pico` (v2) and import `@picocss/pico/css/pico.min.css` in `main.tsx` before `index.css`. Remove Vite template styles from `index.css` that conflict with Pico. Keep only small app-specific rules (for example struck-through completed rows).
  - `App.tsx`: wrap content in `<main className="container">`.
  - `api.ts`: move shared logic into a private `request(method, path, body?)` (token, JSON body, `ApiError` with ProblemDetails). Keep `apiGet<T>` and add `apiSend<T>(method: 'POST' | 'PUT' | 'DELETE', path, body?)` that returns `undefined` for 204.
  - Logging: keep the existing DEV-only `console.debug('[api] …', { method, path, status })`. Never log bodies.

- [x] Task 5: Tasks feature UI (depends on 2, 4)
  - Files: `frontend/src/features/tasks/tasksApi.ts`, `frontend/src/features/tasks/dateTime.ts`, `frontend/src/features/tasks/TaskForm.tsx`, `frontend/src/features/tasks/TaskRow.tsx`, `frontend/src/features/tasks/TasksPage.tsx`, `frontend/src/features/auth/AuthGate.tsx`
  - `tasksApi.ts`: `Task` type matching `TaskResponse`; `listTasks`, `createTask`, `updateTask`, `completeTask`, `reopenTask`, `deleteTask` on top of `apiGet`/`apiSend`.
  - `dateTime.ts`: `toLocalInputValue(iso | null)` / `fromLocalInputValue(value)` (empty → null; else `new Date(value).toISOString()`), `formatDue(iso | null)` (local, short; "—" when null).
  - `TaskForm`: title, notes (textarea), due (`datetime-local`), Submit + optional Cancel. Props: `initial`, `submitLabel`, `onSubmit`, `onCancel?`. Shows `ApiError` ProblemDetails validation messages. Disables submit while saving. Resets after a successful create.
  - `TaskRow`: checkbox (complete/reopen), title, notes as `<small>`, due, Edit / Delete buttons (Pico `secondary outline`). Edit turns the row into `TaskForm` (inline). Delete asks `window.confirm`.
  - `TasksPage`: loads the list on mount, then shows the create form, a "Show completed" switch (`role="switch"`), and the list. Completed tasks are hidden unless the switch is on. After each mutation, replace/insert/remove the returned task in state and re-sort with the same order as the server (or refetch the list — implementer picks the simpler one). Loading, empty ("No tasks yet") and error-with-Retry states.
  - `AuthGate` `ok` state: header row with the email + Sign out, then `<TasksPage />` (replaces the "Hello" text).
  - Logging: DEV-only `console.error('[tasks] … failed', error)` on failed calls. No task content in logs.
  - Verify: `npm run lint` and `npm run build` in `frontend/` pass. Manual: run the API and `npm run dev`, sign in, then create (with and without due date), edit inline, complete, toggle "Show completed", reopen, delete. Check the sort order. Reload and confirm the data persisted. Sign in as a second allowlisted account and confirm the first account's tasks are not visible.

- [x] Task 6: Firebase Auth Emulator for local end-to-end testing (added 2026-11-09 at user request; depends on 5)
  - Goal: sign in locally (and from Playwright) with fake Google accounts, without a real Google login. Then run Task 5's manual scenario in a browser.
  - Files: `frontend/package.json` (devDependency `firebase-tools`, scripts `emulators` and `dev:emulator`), `frontend/firebase.json` (auth emulator on port 9099, UI off), `frontend/.firebaserc` (default project = the existing Firebase project id), `frontend/.env.emulator` (`VITE_USE_AUTH_EMULATOR=1`), `.gitignore` (`!.env.emulator`), `frontend/src/features/auth/firebase.ts`, `backend/Quests.Api/Infrastructure/Auth/FirebaseJwtSetup.cs`, `backend/Quests.Api/Program.cs`, `backend/Quests.Api/Properties/launchSettings.json` (profile `emulator`), `backend/Quests.Tests/TestTokens.cs`, `backend/Quests.Tests/Features/Identity/AuthEmulatorTests.cs`
  - Frontend: when `import.meta.env.DEV` and `VITE_USE_AUTH_EMULATOR === '1'`, call `connectAuthEmulator(auth, 'http://127.0.0.1:9099')`. `dev:emulator` = `vite --mode emulator`.
  - Backend: when `FIREBASE_AUTH_EMULATOR_HOST` is set, accept unsigned emulator tokens (no signing key, no OpenID metadata download) and keep issuer/audience/lifetime validation. Allowed only in the Development environment: in any other environment the API refuses to start. Logs a Warning at startup while emulator mode is on.
  - Tests: Development + emulator host → an unsigned token reaches `/api/me` (200). Emulator host outside Development → startup throws. Without the emulator host, an unsigned token → 401 (existing behavior).
  - Logging: Warning "Auth emulator mode: accepting unsigned tokens"; existing Debug auth-failure logs unchanged.
  - Verify: `dotnet test backend/Quests.slnx`; `npm run lint` and `npm run build`; then run the emulator, the API (`--launch-profile emulator`) and `npm run dev:emulator`, and use Playwright for Task 5's manual scenario, including second-account isolation.

### Phase 3: Documentation

- [ ] Task 7: Docs checkpoint (depends on 3, 6)
  - Files: `README.md`, `AGENTS.md`
  - Run `/aif-docs`: in README, document the tasks API endpoints, the Pico CSS styling choice, and local testing with the Firebase Auth Emulator (Java 21 required for `firebase-tools`; how to start the emulator, the API and the frontend in emulator mode). In AGENTS.md, add `Features/Tasks/` and `frontend/src/features/tasks/` to the structure map and update the "Project Structure" summary line.
  - Logging: n/a.

## Commit Plan
1. After Tasks 1–3: `feat(tasks): add tasks API with owner-scoped CRUD`
2. After Tasks 4–6: `feat(tasks): add task list UI with Pico CSS`
3. After Task 7: `docs: document tasks API and UI`
