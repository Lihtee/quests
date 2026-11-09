# Quests

> A personal task manager with time windows for tasks.

Quests is a web and Android app. Each task can have a hard deadline, a soft deadline, and a desirable period, and a "What to do now" button picks what to work on. It syncs between devices and works offline. So far, the repository has the project skeleton (API, frontend, CI), Google sign-in with an email allowlist, automatic deployment to AWS Lightsail, and a minimal task list (title, notes, optional due date; online-only for now). The UI is styled with [Pico CSS](https://picocss.com/) (classless, built-in light/dark).

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js 24](https://nodejs.org/) (LTS) with npm
- Java 21: optional, only for local testing with the Firebase Auth Emulator ([docs/local-testing.md](docs/local-testing.md))

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

CI runs the same checks on every push and pull request to `main` (see [.github/workflows/ci.yml](.github/workflows/ci.yml)).

## Configuration

| Environment variable | Default | Description |
|----------------------|---------|-------------|
| `LOG_LEVEL` | `Information` | Minimum log level. Allowed: `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical` (any case). An invalid value falls back to `Information` and logs a warning. |
| `ConnectionStrings__Default` | `Data Source=quests.db` (Development only) | SQLite connection string. Outside Development it has no default, and the API refuses to start without it. |
| `FIREBASE_PROJECT_ID` | — (required) | Firebase project id. ID tokens must have this project as audience and `https://securetoken.google.com/<id>` as issuer. The API refuses to start without it. |
| `ALLOWED_EMAILS` | empty | Comma-separated emails allowed to sign in (case-insensitive). Read at startup, so restart to apply changes. Empty means nobody can sign in, and the API logs a warning. |
| `LOG_FILE` | empty (Docker image: `/app/logs/quests.log`) | When set, logs go only to this file (JSON lines, rotated at 10 MB, 5 files kept) and console logging is off. The directory is created if missing. |

Frontend variables go in `frontend/.env.local` (git-ignored; template: [frontend/.env.example](frontend/.env.example)): `VITE_FIREBASE_API_KEY`, `VITE_FIREBASE_AUTH_DOMAIN`, `VITE_FIREBASE_PROJECT_ID`, `VITE_FIREBASE_APP_ID`.

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

## Tasks API

All endpoints act only on the signed-in user's tasks; another user's task id returns `404`. Body for create/update: `{ "title", "notes", "dueAt" }` (title required, ≤ 200 chars after trimming; notes ≤ 4000; `dueAt` optional ISO 8601, stored as UTC).

| Endpoint | Success | Errors |
|----------|---------|--------|
| `GET /api/tasks` | `200` all tasks: open first (earliest due first, undated last), then completed (newest first) | `401` |
| `POST /api/tasks` | `201` task + `Location` | `400` validation, `401` |
| `PUT /api/tasks/{id}` | `200` task (full replace) | `400`, `401`, `404` |
| `POST /api/tasks/{id}/complete` | `200` task | `401`, `404` |
| `POST /api/tasks/{id}/reopen` | `200` task | `401`, `404` |
| `DELETE /api/tasks/{id}` | `204` (hard delete) | `401`, `404` |

A task is `{ "id", "title", "notes", "dueAt", "completedAt", "createdAt", "updatedAt" }`.

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

## Project Layout

| Path | Contents |
|------|----------|
| `backend/Quests.Api` | ASP.NET Core Minimal API (EF Core + SQLite); auth in `Features/Identity/` and `Infrastructure/Auth/`, tasks in `Features/Tasks/` |
| `backend/Quests.Tests` | xUnit tests |
| `frontend/` | React + TypeScript (Vite) app with Pico CSS; sign-in in `src/features/auth/`, task list in `src/features/tasks/`, API client in `src/shared/api.ts`; Auth Emulator config in `firebase.json`, `.firebaserc`, `.env.emulator` |
| `.github/workflows/` | CI and deploy |
| `Dockerfile`, `.dockerignore` | Production image (API + React build) |
| `deploy/` | Compose stack and Caddyfile used on the instance |
| `docs/` | Detailed documentation ([local testing](docs/local-testing.md), [deployment](docs/deployment.md)) |
| `.config/dotnet-tools.json` | Local .NET tools (`dotnet-ef`) |
