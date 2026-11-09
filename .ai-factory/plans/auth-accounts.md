# Implementation Plan: Auth & accounts

Branch: none (git.create_branches=false)
Created: 2026-11-09

## Original Request
Auth & accounts

## Settings
- Testing: yes
- Logging: verbose
- Docs: yes
- Commit strategy: incremental
- Development methodology: implementation-first

## Roadmap Linkage
Milestone: "Auth & accounts"
Rationale: The user named this roadmap milestone as the planning request.

## Decisions (confirmed by user)
- Allowlist is stored in config: env var `ALLOWED_EMAILS` (comma-separated). Changing it requires a restart.
- Users table with `Guid Id`: a `User` row (Id, FirebaseUid, Email, CreatedAt) is created on first allowed sign-in. Future data rows use `OwnerId Guid`.
- Scoping: `CurrentUser` service + `IOwnedEntity` interface. `AppDbContext` auto-applies an `OwnerId` query filter to every `IOwnedEntity`. The first real owned entity and filter test come with the "Minimal tasks" milestone.
- UI: Google sign-in button; after sign-in `GET /api/me` shows "Hello, <email>" + sign-out; non-allowlisted users see "You are not invited" + sign-out.
- Out of scope: sign-in on Android (Capacitor) — "Android app" milestone; deployment/HTTPS — "Deployment" milestone; frontend unit tests (Vitest is not set up; the auth UI is thin and is verified by hand).

## Requirements Reconciliation
Authority: none declared. `ROADMAP.md` (Auth & accounts), `DESCRIPTION.md` (Tech Stack: Auth; NFR: Security) and `ARCHITECTURE.md` (Identity module, Key Principle 3) agree; ARCHITECTURE refines file placement.

| Supported combination (token × email_verified × allowlist) | Source | Result | Verification |
|---|---|---|---|
| No token / bad signature / wrong issuer or audience / expired | DESCRIPTION — "every API/hub call requires a valid Firebase JWT" | 401 ProblemDetails, no User row | AuthTests |
| Valid token, `email_verified` false | DESCRIPTION — "email allowlist for access" (unverified email cannot prove ownership) | 403 ProblemDetails, no User row | AuthTests |
| Valid, verified, email not in allowlist | ROADMAP — "email allowlist" | 403 ProblemDetails, no User row | AuthTests |
| Valid, verified, allowlisted (case-insensitive), first call | ROADMAP — "all data scoped by user id" | 200 `{ id, email }`, User row created | AuthTests |
| Same user, repeat call | — | 200, same `id`, no duplicate row | AuthTests |
| `/api/health` without token | Skeleton contract | 200 (anonymous) | AuthTests |

Meaning preservation: PASS

## Tasks

### Phase 1: Backend authentication and accounts

- [x] Task 1: Firebase JWT authentication setup
  - Files: `backend/Quests.Api/Quests.Api.csproj`, `backend/Quests.Api/Infrastructure/Auth/FirebaseJwtSetup.cs`
  - Add package `Microsoft.AspNetCore.Authentication.JwtBearer` (10.0.x, matching the EF Core version line).
  - `AddQuestsAuthentication(IServiceCollection, IConfiguration)`: read `FIREBASE_PROJECT_ID`; if missing, throw `InvalidOperationException` with a clear message (same style as `DataSetup`). Configure JwtBearer: `Authority = https://securetoken.google.com/<projectId>`, `ValidIssuer` = same URL, `ValidAudience = <projectId>`, validate lifetime, `MapInboundClaims = false` (keep `sub`, `email`, `email_verified` as-is).
  - Logging: Debug on startup "Firebase JWT auth configured for project {ProjectId}"; JwtBearer events: `OnAuthenticationFailed` → Debug with exception type/message only; `OnChallenge` → Debug with the request path. Never log the token or the Authorization header.

- [x] Task 2: Email allowlist authorization (fallback policy) (depends on 1)
  - Files: `backend/Quests.Api/Features/Identity/AllowlistAuthorization.cs`
  - `AllowlistRequirement` + `AllowlistHandler`: succeeds only when the user is authenticated, `email_verified` claim is `true`, and `email` is in the allowlist (trimmed, case-insensitive).
  - Allowlist from config `ALLOWED_EMAILS` (comma-separated), parsed once into a singleton `HashSet<string>` with `StringComparer.OrdinalIgnoreCase`.
  - Register in `AddQuestsAuthorization()`: `FallbackPolicy` = authenticated + `AllowlistRequirement`, so every endpoint (and the future SignalR hub) is protected by default.
  - Logging: Information at startup "Allowlist loaded with {Count} emails" (count only); Warning if the list is empty ("nobody can sign in"); Warning on denial "Access denied for {FirebaseUid}: {Reason}" (reason = `email_not_verified` / `not_allowlisted`); the email itself only at Debug.

- [x] Task 3: `User` entity and migration
  - Files: `backend/Quests.Api/Features/Identity/User.cs`, `backend/Quests.Api/Features/Identity/UserConfiguration.cs`, `backend/Quests.Api/Infrastructure/Data/Migrations/<timestamp>_AddUsers.cs` (+ Designer, snapshot)
  - `User`: `Guid Id`, `string FirebaseUid`, `string Email`, `DateTimeOffset CreatedAt`; factory `User.Create(firebaseUid, email, now)` and `UpdateEmail(email)` (no clock access inside the model). `AppDbContext` gets `DbSet<User> Users`.
  - `UserConfiguration : IEntityTypeConfiguration<User>`: unique index on `FirebaseUid`, required fields, max lengths. `User` is NOT an `IOwnedEntity`.
  - Create the migration with `dotnet ef migrations add AddUsers` (local tool from `.config/dotnet-tools.json`, output dir `Infrastructure/Data/Migrations`).
  - Logging: none in the model (pure); migration logging already exists in `DataSetup`.

- [x] Task 4: `CurrentUser`, user provisioning, owner query filter convention (depends on 3)
  - Files: `backend/Quests.Api/Features/Identity/CurrentUser.cs`, `backend/Quests.Api/Features/Identity/UserProvisioningMiddleware.cs`, `backend/Quests.Api/Infrastructure/Data/IOwnedEntity.cs`, `backend/Quests.Api/Infrastructure/Data/AppDbContext.cs`
  - `CurrentUser` (scoped): `Guid? Id`, `string? Email`; set once per request by the middleware.
  - `UserProvisioningMiddleware` (runs after `UseAuthorization`): only for authenticated requests that passed authorization (`context.User.Identity.IsAuthenticated` and endpoint not anonymous). Look up `User` by `sub`; if missing, create it with `TimeProvider` time; if the email changed, update it. Handle the first-sign-in race: on `DbUpdateException` from the unique index, re-read the row. Store `Id`/`Email` in `CurrentUser`.
  - `IOwnedEntity { Guid OwnerId { get; } }`. In `AppDbContext.OnModelCreating`, for every entity type implementing `IOwnedEntity`, apply `HasQueryFilter(e => e.OwnerId == CurrentOwnerId)` via a built expression; `CurrentOwnerId` reads the injected `CurrentUser` (null when there is no request, e.g. startup migrations → filter matches nothing, fail closed).
  - Register `TimeProvider.System` as singleton if not already registered.
  - Logging: Information "User provisioned {UserId}" on creation; Debug "Resolved user {UserId} for {FirebaseUid}" per request; Debug on email update; Warning on the unique-index race retry.

- [x] Task 5: `GET /api/me` and `Program.cs` wiring (depends on 1, 2, 4)
  - Files: `backend/Quests.Api/Features/Identity/MeEndpoints.cs`, `backend/Quests.Api/Program.cs`, `backend/Quests.Api/Infrastructure/Errors/ProblemDetailsSetup.cs` (only if needed)
  - `MapMeEndpoints()`: `GET /api/me` → `{ id, email }` record from `CurrentUser` (thin handler, no DB query).
  - `Program.cs`: `AddQuestsAuthentication`, `AddQuestsAuthorization`, `AddScoped<CurrentUser>`; pipeline `UseAuthentication` → `UseAuthorization` → `UseMiddleware<UserProvisioningMiddleware>`; `MapHealthChecks("/api/health").AllowAnonymous()`; `MapMeEndpoints()`.
  - Make sure 401/403 bodies are RFC 7807 ProblemDetails (check that the existing `ProblemDetailsSetup` status-code handling covers them; add it if not).
  - Logging: Debug "GET /api/me for {UserId}".

- [x] Task 6: API tests for auth (depends on 5)
  - Files: `backend/Quests.Tests/ApiFactory.cs`, `backend/Quests.Tests/TestTokens.cs`, `backend/Quests.Tests/Features/Identity/AuthTests.cs`
  - `ApiFactory`: set `FIREBASE_PROJECT_ID=test-project`, `ALLOWED_EMAILS=allowed@test.local`; `PostConfigure<JwtBearerOptions>` to drop `Authority`/metadata and validate with a test symmetric key, keeping the real issuer/audience values.
  - `TestTokens.Create(sub, email, emailVerified, expires, audience?, issuer?, signingKey?)` using `Microsoft.IdentityModel.JsonWebTokens` (comes with JwtBearer).
  - Cover every row of the Requirements Reconciliation table, plus case-insensitive allowlist match (`ALLOWED@test.local`). Assert ProblemDetails content type on 401/403 and User row count via `AppDbContext`.
  - Logging: no extra logging in tests; rely on the API logs on failure.

### Phase 2: Frontend sign-in

- [x] Task 7: Firebase client and auth hook (depends on 5)
  - Files: `frontend/package.json`, `frontend/src/features/auth/firebase.ts`, `frontend/src/features/auth/useAuth.ts`, `frontend/.env.example`, `frontend/src/vite-env.d.ts` (env typings, if the file exists — otherwise add typings in `firebase.ts`)
  - Add `firebase` dependency. `firebase.ts` initializes the app from `VITE_FIREBASE_API_KEY`, `VITE_FIREBASE_AUTH_DOMAIN`, `VITE_FIREBASE_PROJECT_ID`, `VITE_FIREBASE_APP_ID`; exports `auth`.
  - `useAuth()`: `{ user, loading, signIn, signOut }` using `onAuthStateChanged`, `signInWithPopup(GoogleAuthProvider)`, `signOut`.
  - `.env.example` lists the 4 vars with placeholder values; real values go in `.env.local` (git-ignored by the Vite template's `*.local`).
  - Logging: dev-only `console.debug` on auth state change (signed in / signed out, no token, no email); `console.error` on sign-in failure.

- [x] Task 8: Authenticated fetch wrapper (depends on 7)
  - Files: `frontend/src/shared/api.ts`
  - `apiGet<T>(path)`: gets `await auth.currentUser.getIdToken()`, sends `Authorization: Bearer …`; on non-2xx throws `ApiError { status, problem }` (parsed ProblemDetails when present).
  - Logging: dev-only `console.debug` of method, path, status; never log the token.

- [x] Task 9: Sign-in / hello / not-invited UI (depends on 7, 8)
  - Files: `frontend/src/features/auth/AuthGate.tsx`, `frontend/src/App.tsx`
  - States: loading → signed out ("Sign in with Google" button) → signed in: call `GET /api/me`: 200 → "Hello, {email}" + "Sign out"; 403 → "You are not invited" + "Sign out"; other errors → short error text + retry button.
  - Plain markup, no UI library (styling approach is still an open decision).
  - Logging: dev-only `console.error` for unexpected `/api/me` failures.
  - Manual check: run API with `FIREBASE_PROJECT_ID` + `ALLOWED_EMAILS` and `npm run dev`; sign in with an allowlisted Google account → hello page; with another account → not invited.

### Phase 3: Documentation

- [x] Task 10: Document auth setup (`/aif-docs` checkpoint) (depends on 1–9)
  - Files: `README.md`, `AGENTS.md`
  - README: Firebase project setup (create project, enable Google provider, add `localhost` to authorized domains, register a web app), backend env vars `FIREBASE_PROJECT_ID`, `ALLOWED_EMAILS`, frontend `.env.local` vars, how 401/403 behave.
  - AGENTS.md: add `Features/Identity/`, `Infrastructure/Auth/`, `frontend/src/features/auth/`, `frontend/src/shared/api.ts` to the structure map.

## Commit Plan
- **Commit 1** (after tasks 1-6): "feat: add Firebase JWT auth, email allowlist and user accounts"
- **Commit 2** (after tasks 7-9): "feat: add Google sign-in and signed-in hello page"
- **Commit 3** (after task 10): "docs: document Firebase auth setup"
