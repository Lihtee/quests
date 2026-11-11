# Quests

> A personal task manager with time windows for tasks.

Quests is a web and Android app. Each task can have a hard deadline, a soft deadline, and a desirable period, and a "What to do now" button picks what to work on. It syncs between devices and works offline. So far, the repository has the project skeleton (API, frontend, CI), Google sign-in with an email allowlist, automatic deployment to AWS Lightsail, a minimal task list (title, notes, optional due date) that works offline and syncs between devices (open devices update within seconds), and an Android app (Capacitor, native Google sign-in, signed APK from CI). The UI is styled with [Pico CSS](https://picocss.com/) (classless, built-in light/dark).

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js 24](https://nodejs.org/) (LTS) with npm
- Java 21: optional, for local testing with the Firebase Auth Emulator ([docs/local-testing.md](docs/local-testing.md)) and local Android builds ([docs/android.md](docs/android.md), also needs the Android SDK)

## Quick Start

First set up a Firebase project (see [Authentication](#authentication)).

Start the API (http://localhost:5080):

```bash
# once per machine: who may sign in (kept in user-secrets, not in git)
dotnet user-secrets set ALLOWED_EMAILS you@gmail.com --project backend/Quests.Api
dotnet run --project backend/Quests.Api
curl http://localhost:5080/api/health   # → Healthy
```

In Development, `FIREBASE_PROJECT_ID` comes from `appsettings.Development.json`.

On startup, the API creates the SQLite database `backend/Quests.Api/quests.db` and applies all migrations.

Start the frontend in a second terminal:

```bash
cd frontend
cp .env.example .env.local   # then fill in your Firebase web app values
npm install
npm run dev
```

The Vite dev server forwards `/api` requests to the API at `http://localhost:5080`. Open the app and sign in with Google. An allowlisted account sees its task list. Any other account sees "You are not invited".

To test without a real Google login (fake accounts, browser automation), use the Firebase Auth Emulator: see [docs/local-testing.md](docs/local-testing.md).

## Tests and Checks

| Command | What it checks |
|---------|----------------|
| `dotnet test backend/Quests.slnx` | Backend xUnit tests |
| `npm run lint` (in `frontend/`) | Frontend lint (oxlint) |
| `npm run build` (in `frontend/`) | Frontend type check and production build |
| `npm test` (in `frontend/`) | Frontend Vitest tests (task rules, sync engine on an in-memory IndexedDB, API base URL, native/web sign-in routing) |

CI runs the same checks on every push and pull request to `main` (see [.github/workflows/ci.yml](.github/workflows/ci.yml)).

## Configuration

| Environment variable | Default | Description |
|----------------------|---------|-------------|
| `LOG_LEVEL` | `Information` | Minimum log level. Allowed: `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical` (any case). An invalid value falls back to `Information` and logs a warning. |
| `ConnectionStrings__Default` | `Data Source=quests.db` (Development only) | SQLite connection string. Outside Development it has no default, and the API refuses to start without it. |
| `FIREBASE_PROJECT_ID` | — (required) | Firebase project id. ID tokens must have this project as audience and `https://securetoken.google.com/<id>` as issuer. The API refuses to start without it. |
| `ALLOWED_EMAILS` | empty | Comma-separated emails allowed to sign in (case-insensitive). Read at startup, so restart to apply changes. Empty means nobody can sign in, and the API logs a warning. |
| `LOG_FILE` | empty (Docker image: `/app/logs/quests.log`) | When set, logs go only to this file (JSON lines, rotated at 10 MB, 5 files kept) and console logging is off. The directory is created if missing. |
| `CORS_ORIGINS` | empty (production compose: `https://localhost`) | Comma-separated origins allowed to call the API and hub from another origin (the Android app's `https://localhost`). Credentials are never allowed (bearer token only). Entries that aren't absolute `http(s)` origins are skipped with a warning. |

Frontend variables go in `frontend/.env.local` (git-ignored; template: [frontend/.env.example](frontend/.env.example)): `VITE_FIREBASE_API_KEY`, `VITE_FIREBASE_AUTH_DOMAIN`, `VITE_FIREBASE_PROJECT_ID`, `VITE_FIREBASE_APP_ID`, and optional `VITE_API_BASE_URL` (absolute server URL for API and hub calls; empty for the web app, `https://<DOMAIN>` for Android builds).

Logs are written as JSON, one line per entry: to the console, or to `LOG_FILE` when it is set.

The API also serves the React build from `wwwroot` (present in the Docker image, not in local dev). Unknown `/api/*` paths return ProblemDetails, never the web app.

## Authentication

Sign-in uses Firebase Authentication with Google. One-time Firebase setup:

1. Create a project in the [Firebase console](https://console.firebase.google.com/).
2. **Authentication → Sign-in method:** enable **Google**.
3. **Authentication → Settings → Authorized domains:** make sure `localhost` is listed.
4. **Project settings → Your apps:** register a **Web app** and copy its config values into `frontend/.env.local`.

How the API handles requests:

| Request | Response |
|---------|----------|
| `GET /api/health` | Always allowed, no token needed |
| No token, invalid signature, wrong issuer/audience, or expired token | `401` ProblemDetails |
| Valid token, but email not verified or not in `ALLOWED_EMAILS` | `403` ProblemDetails |
| Valid token with a verified, allowlisted email | Request proceeds; `GET /api/me` returns `{ "id", "email" }` |

Every route except `/api/health` requires sign-in. On the first allowed request, the API creates a user row; user data is scoped by that `id`. Error bodies are RFC 7807 ProblemDetails (`application/problem+json`).

## Offline-First Sync

The UI reads and writes only a local IndexedDB database (Dexie, one per user: `quests-<uid>`). Each write also adds an entry to a local outbox. The sync engine sends the outbox to the server and pulls changes. It runs on app start, window focus, when the browser comes back online, every 60 s while the page is visible, and about 500 ms after each local write. If a sync fails, the outbox is kept and the next trigger retries. Offline works while the tab stays open. Reloading the page offline is not supported yet (no service worker).

`POST /api/sync` is the only task endpoint. It acts only on the signed-in user's tasks (`401` without a token, `400` for a malformed body or negative `since`).

Request:

```json
{
  "since": 41,
  "changes": [
    { "id": "<client uuid>", "op": "upsert", "baseVersion": 40, "changed": ["title"],
      "title": "New title", "notes": null, "dueAt": null, "completedAt": null, "createdAt": "2026-11-09T08:00:00Z" },
    { "id": "<uuid>", "op": "delete", "baseVersion": 38, "changed": [] }
  ]
}
```

Response: `{ "version": 44, "tasks": [ ... ], "rejected": [{ "id", "reason" }] }`. `version` is the client's next `since`. `tasks` lists the user's tasks changed after `since` (a first pull with `since: 0` skips deleted tasks), plus the current state of any task whose change was ignored or rejected. A task is `{ "id", "title", "notes", "dueAt", "completedAt", "createdAt", "updatedAt", "version", "deletedAt" }`.

Rules:

| Case | Result |
|------|--------|
| `upsert`, unknown id | Creates the task. Clients make the id; `title` must be in `changed`. |
| `upsert`, existing task | Applies only the fields in `changed` (`title`, `notes`, `dueAt`, `completedAt`). Changes apply in arrival order, so the device that syncs last wins for the fields it changed. Other fields keep their values. |
| `upsert`, deleted task | Restores the task and applies the change. |
| `delete`, task unchanged since `baseVersion` | Soft delete: sets `deletedAt` and keeps the row (a later edit can restore it). |
| `delete`, task changed after `baseVersion` | Ignored ("preserve beats delete"). The task is returned, so it comes back on that device. |
| `delete`, unknown or already deleted | No-op |
| Invalid values (title blank or > 200 chars after trimming, notes > 4000, unknown field or `op`) | Rejected with reason `invalid`. Nothing is saved. |
| Task id owned by another user | Rejected with reason `forbidden`. The other user's task is not changed or returned. |

Each user has a version counter. Every applied change gives the task the next number.

### Real-Time Updates

While the page is visible, the app keeps a SignalR connection to `/api/hubs/sync` (same auth as the API; the browser sends the Firebase ID token in the `access_token` query, which the API accepts only for `/api/hubs/*`). The hub is a "doorbell": after a sync that applied at least one change, the server sends `changed` (no data) to the user's other connections, and they run a sync at once. The app sends its connection id in the `X-Sync-Connection-Id` header of `POST /api/sync`, so the device that made the change is not signalled. The connection closes when the page is hidden and opens again (with a catch-up sync) when it becomes visible. A missed signal is harmless: the other sync triggers still run.

## Database Migrations

Run these from `backend/` so that `backend/nuget.config` applies:

```bash
cd backend
dotnet tool restore
dotnet ef migrations add <Name> --project Quests.Api --output-dir Infrastructure/Data/Migrations
```

The API applies new migrations on its next start. `dotnet ef` does not need the auth variables: it creates the context through `AppDbContextDesignTimeFactory`, not `Program.cs`.

> `backend/nuget.config` limits package sources to nuget.org, so private feeds in your user-level NuGet config don't break the restore.

## Deployment

Every push to `main` builds a Docker image and deploys it to AWS Lightsail behind Caddy (HTTPS). One-time setup, GitHub secrets, logs and rollback: [docs/deployment.md](docs/deployment.md).

The same workflow also builds a signed Android APK (Capacitor) and uploads it as an artifact. Keystore, Firebase Android setup, local builds and installing: [docs/android.md](docs/android.md).

## Project Layout

| Path | Contents |
|------|----------|
| `backend/Quests.Api` | ASP.NET Core Minimal API (EF Core + SQLite); auth in `Features/Identity/` and `Infrastructure/Auth/`, task model in `Features/Tasks/`, `POST /api/sync` and the SignalR hub in `Features/Sync/` |
| `backend/Quests.Tests` | xUnit tests |
| `frontend/` | React + TypeScript (Vite) app with Pico CSS; sign-in in `src/features/auth/`, task list and local writes in `src/features/tasks/`, sync engine and SignalR client in `src/features/sync/`, pure task rules in `src/domain/`, Dexie schema in `src/shared/db.ts`, API client in `src/shared/api.ts`; Auth Emulator config in `firebase.json`, `.firebaserc`, `.env.emulator`; Capacitor config in `capacitor.config.ts` and the Android project in `android/` |
| `.github/workflows/` | CI and deploy |
| `Dockerfile`, `.dockerignore` | Production image (API + React build) |
| `deploy/` | Compose stack and Caddyfile used on the instance |
| `docs/` | Detailed documentation ([local testing](docs/local-testing.md), [deployment](docs/deployment.md), [Android app](docs/android.md)) |
| `.config/dotnet-tools.json` | Local .NET tools (`dotnet-ef`) |
