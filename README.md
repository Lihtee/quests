# Quests

> A personal task manager with time windows for tasks.

Quests is a web and Android app. Each task can have a hard deadline, a soft deadline, and a desirable period, and a "What to do now" button picks what to work on. It syncs between devices and works offline. This repository currently contains only the project skeleton: an API, a frontend placeholder, and CI.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- [Node.js 24](https://nodejs.org/) (LTS) with npm

## Quick Start

Start the API (http://localhost:5080):

```bash
dotnet run --project backend/Quests.Api
curl http://localhost:5080/api/health   # → Healthy
```

On startup, the API creates the SQLite database `backend/Quests.Api/quests.db` and applies all migrations.

Start the frontend in a second terminal:

```bash
cd frontend
npm install
npm run dev
```

The Vite dev server forwards `/api` requests to the API at `http://localhost:5080`.

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

Logs are written to the console as JSON, one line per entry.

## Database Migrations

Run these from `backend/` so that `backend/nuget.config` applies:

```bash
cd backend
dotnet tool restore
dotnet ef migrations add <Name> --project Quests.Api --output-dir Infrastructure/Data/Migrations
```

The API applies new migrations on its next start.

> `backend/nuget.config` limits package sources to nuget.org, so private feeds in your user-level NuGet config don't break the restore.

## Project Layout

| Path | Contents |
|------|----------|
| `backend/Quests.Api` | ASP.NET Core Minimal API (EF Core + SQLite) |
| `backend/Quests.Tests` | xUnit tests |
| `frontend/` | React + TypeScript (Vite) app |
| `.github/workflows/` | CI |
| `.config/dotnet-tools.json` | Local .NET tools (`dotnet-ef`) |
