# Implementation Plan: Project skeleton

Branch: main (git.create_branches: false — ask the user before creating extra branches)
Created: 2026-11-09

## Original Request
Project skeleton

## Settings
- Testing: yes — backend xUnit only (no Vitest in this milestone)
- Logging: verbose
- Docs: yes
- Commit strategy: incremental
- Development methodology: implementation-first

## Roadmap Linkage
Milestone: "Project skeleton"
Rationale: The request is exactly the first Foundation milestone in ROADMAP.md.

## Decisions
- Structured logging: built-in `Microsoft.Extensions.Logging` with the JSON console formatter; minimum level from `LOG_LEVEL` env var (no Serilog).
- Frontend package manager: npm.
- Backend tests: xUnit + `Microsoft.AspNetCore.Mvc.Testing` (`WebApplicationFactory`).
- Out of scope (later milestones): auth, serving the React build from the API, deployment, Dexie, Capacitor, any task entities.

## Assumptions (confirmed by user 2026-11-09)
These are small infrastructure choices made to keep the skeleton verifiable.
1. Health check: built-in `AddHealthChecks()` mapped at `GET /api/health` (used by tests, CI and later deployment).
2. EF Core migrations are applied automatically on startup (`Database.MigrateAsync()`); fine for a single-instance SQLite app.
3. API dev port fixed at `http://localhost:5080` in `launchSettings.json`; Vite dev server proxies `/api` there.
4. Solution file uses the .NET 10 default `.slnx` format.
5. CI runs on push and pull request to `main`; it only runs once the repo is pushed to GitHub (no remote exists yet).

## Tasks

### Phase 1: Backend skeleton
- [x] Task 1: Create the .NET 10 solution and API project
  - `dotnet new sln` → `backend/Quests.slnx`; `dotnet new web` → `backend/Quests.Api` (Minimal API, nullable + implicit usings on).
  - Create `Features/` and `Infrastructure/` folders per ARCHITECTURE.md (no feature code yet; add a folder only when it gets a file).
  - `Program.cs` is the composition root: calls setup extension methods from Tasks 2–4 and maps `/api/health` via `AddHealthChecks()` / `MapHealthChecks("/api/health")`.
  - `Properties/launchSettings.json`: single `http` profile on `http://localhost:5080`.
  - Add `public partial class Program;` at the end of `Program.cs` so tests can use `WebApplicationFactory<Program>`.
  - Logging: log `Information` on startup with environment name (built-in host logs are enough; no custom code).
  - Files: `backend/Quests.slnx`, `backend/Quests.Api/Quests.Api.csproj`, `backend/Quests.Api/Program.cs`, `backend/Quests.Api/Properties/launchSettings.json`, `backend/Quests.Api/appsettings.json`, `backend/Quests.Api/appsettings.Development.json`

- [x] Task 2: Structured logging with `LOG_LEVEL` (depends on 1)
  - `Infrastructure/Logging/LoggingSetup.cs`: extension `AddQuestsLogging(this WebApplicationBuilder)` → `ClearProviders()`, `AddJsonConsole()` (timestamp, single-line, scopes on), minimum level parsed from `LOG_LEVEL` (`Trace|Debug|Information|Warning|Error|Critical`, case-insensitive); default `Information`; invalid value → keep default and log a `Warning` once after startup.
  - Keep `Microsoft.AspNetCore` / `Microsoft.EntityFrameworkCore` categories at `Warning` in `appsettings.json` so `LOG_LEVEL=Debug` stays readable.
  - Logging: `Debug` log of the effective minimum level at startup. Never log tokens or task content at `Information`+.
  - Files: `backend/Quests.Api/Infrastructure/Logging/LoggingSetup.cs`, `backend/Quests.Api/Program.cs`, `backend/Quests.Api/appsettings.json`

- [x] Task 3: ProblemDetails error handling (depends on 1)
  - `Infrastructure/Errors/ProblemDetailsSetup.cs`: `AddQuestsProblemDetails()` → `AddProblemDetails()` adding `traceId` to every response via `CustomizeProblemDetails`; `UseQuestsProblemDetails()` → `UseExceptionHandler()` + `UseStatusCodePages()`.
  - Result: unhandled exceptions → 500 `application/problem+json` (no stack trace outside Development); unknown routes → 404 `application/problem+json`.
  - Logging: unhandled exceptions logged at `Error` with `traceId` and request path (the built-in exception handler middleware does this — verify it, add nothing if it does).
  - Files: `backend/Quests.Api/Infrastructure/Errors/ProblemDetailsSetup.cs`, `backend/Quests.Api/Program.cs`

- [x] Task 4: EF Core + SQLite (depends on 1)
  - Packages: `Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.EntityFrameworkCore.Design` (latest 10.x).
  - Local tool manifest with `dotnet-ef` (`.config/dotnet-tools.json` at repo root).
  - `Infrastructure/Data/AppDbContext.cs`: empty `DbContext` (no entities yet), `ApplyConfigurationsFromAssembly`.
  - `Infrastructure/Data/DataSetup.cs`: `AddQuestsData()` registers `AppDbContext` with connection string `ConnectionStrings:Default` (env var `ConnectionStrings__Default`); `appsettings.Development.json` default `Data Source=quests.db`; missing connection string → fail fast at startup with a clear message. `MigrateQuestsDatabaseAsync()` applies migrations on startup.
  - Create initial (empty) migration `Initial` into `Infrastructure/Data/Migrations/`.
  - Logging: `Information` "Database migrations applied" with count of applied migrations; `Debug` the SQLite data source path; `Error` on migration failure, then rethrow.
  - Files: `.config/dotnet-tools.json`, `backend/Quests.Api/Quests.Api.csproj`, `backend/Quests.Api/Infrastructure/Data/AppDbContext.cs`, `backend/Quests.Api/Infrastructure/Data/DataSetup.cs`, `backend/Quests.Api/Infrastructure/Data/Migrations/*`, `backend/Quests.Api/appsettings.Development.json`, `backend/Quests.Api/Program.cs`

### Phase 2: Backend tests
- [x] Task 5: xUnit smoke tests (depends on 2, 3, 4)
  - `dotnet new xunit` → `backend/Quests.Tests`, add to `Quests.slnx`, reference `Quests.Api`, add `Microsoft.AspNetCore.Mvc.Testing`.
  - `ApiFactory : WebApplicationFactory<Program>` sets `ConnectionStrings:Default` to a unique temp SQLite file per test run and deletes it on dispose.
  - Tests: `GET /api/health` → 200; `GET /api/does-not-exist` → 404 with `application/problem+json` and a `traceId` field; app starts with migrations applied (implicit in the two tests above).
  - Logging: no custom test logging; failures must show response body in assertion messages.
  - Files: `backend/Quests.Tests/Quests.Tests.csproj`, `backend/Quests.Tests/ApiFactory.cs`, `backend/Quests.Tests/Infrastructure/SmokeTests.cs`, `backend/Quests.slnx`
  - Verify: `dotnet test backend/Quests.slnx` passes; `dotnet run --project backend/Quests.Api` then `curl http://localhost:5080/api/health` returns `Healthy`; with `LOG_LEVEL=Debug` the JSON logs show Debug lines.

### Phase 3: Frontend skeleton
- [x] Task 6: React + Vite + TypeScript app (independent of 1–5)
  - `npm create vite@latest frontend -- --template react-ts`, then `npm install`; commit `package-lock.json`.
  - Remove demo content (counter, logos, demo CSS); `App.tsx` renders a plain "Quests" placeholder heading.
  - Create `src/features/`, `src/domain/`, `src/shared/` only when they get files — none in this task.
  - `vite.config.ts`: dev server proxy `/api` → `http://localhost:5080`.
  - Keep the linter from the template (create-vite 9 ships oxlint, kept per user decision); `npm run lint` and `npm run build` must pass.
  - Logging: none in this task (frontend rule: minimal `console` logging in development only).
  - Files: `frontend/package.json`, `frontend/package-lock.json`, `frontend/vite.config.ts`, `frontend/index.html`, `frontend/src/main.tsx`, `frontend/src/App.tsx`, `frontend/src/index.css`, `frontend/.oxlintrc.json`, `frontend/tsconfig*.json`

### Phase 4: CI
- [x] Task 7: GitHub Actions workflow (depends on 5, 6)
  - `.github/workflows/ci.yml`, triggers: `push` and `pull_request` on `main`.
  - Job `backend` (ubuntu-latest): `actions/setup-dotnet` with `10.0.x`, `dotnet restore`, `dotnet build --no-restore -c Release`, `dotnet test --no-build -c Release` on `backend/Quests.slnx`.
  - Job `frontend` (ubuntu-latest, `working-directory: frontend`): `actions/setup-node` with Node 24 LTS and npm cache, `npm ci`, `npm run lint`, `npm run build`.
  - Logging: use `--logger "console;verbosity=normal"` for `dotnet test` so failures are readable in CI logs.
  - Files: `.github/workflows/ci.yml`

### Phase 5: Documentation
- [x] Task 8: Docs checkpoint via /aif-docs (depends on 1–7)
  - `README.md`: what Quests is (1–2 lines), prerequisites (.NET 10 SDK, Node 24), how to run API and frontend locally, how to run tests, env vars (`LOG_LEVEL`, `ConnectionStrings__Default`), how to add a migration (`dotnet ef migrations add <Name> --project backend/Quests.Api --output-dir Infrastructure/Data/Migrations`).
  - `AGENTS.md`: replace "No application code exists yet" with the real top-level structure (backend/, frontend/, .github/, .config/).
  - Files: `README.md`, `AGENTS.md`

<!-- /aif-verify 2026-11-09: accepted as-is (non-blocking):
W1 500 ProblemDetails path not exercised by a test (built-in behavior, static check only);
W2 DataSetup parses connection string outside the migration try block;
W3 LOG_LEVEL=Error/Critical does not suppress Microsoft.AspNetCore/EF Core warnings (appsettings pins them at Warning);
W4 ARCHITECTURE.md folder tree lacks Infrastructure/Logging/ and backend/nuget.config (owner: /aif-architecture);
W5 frontend/public/favicon.svg is still the Vite logo. -->

## Commit Plan
- **Commit 1** (after tasks 1-5): "feat: add .NET 10 API skeleton with EF Core SQLite, ProblemDetails and JSON logging"
- **Commit 2** (after task 6): "feat: add React + Vite frontend skeleton"
- **Commit 3** (after task 7): "ci: add GitHub Actions build and test workflow"
- **Commit 4** (after task 8): "docs: add README and update AGENTS.md structure"
