# AGENTS.md

> Structural map of the project for AI agents. Keep it factual and update it when the project structure changes significantly.

## Project Overview

Quests is a personal task manager (web + Android) with time-window tasks (hard/soft deadline, desirable period), recurring tasks, a "What to do now" button, phone notifications, and offline-first sync. See `.ai-factory/DESCRIPTION.md` for details.

## Tech Stack

- **Programming language:** C# (.NET 10), TypeScript
- **Framework:** ASP.NET Core Web API + SignalR; React (Vite); Capacitor (Android)
- **Database:** SQLite (server), IndexedDB via Dexie (clients)
- **ORM:** EF Core

## Project Structure

Project skeleton + auth & accounts (Firebase JWT, email allowlist, users) + deployment (Docker, Caddy, CI deploy to Lightsail) + minimal tasks (owner-scoped CRUD API, online-only React UI with Pico CSS) + Firebase Auth Emulator for local testing. Detailed target layout: `.ai-factory/ARCHITECTURE.md`.

```
quests/
├── backend/
│   ├── Quests.slnx                   # .NET solution
│   ├── nuget.config                  # Restricts NuGet sources to nuget.org
│   ├── Quests.Api/                   # ASP.NET Core Minimal API
│   │   ├── Program.cs                # Composition root
│   │   ├── Features/
│   │   │   ├── Identity/             # User, allowlist policy, CurrentUser, provisioning middleware, GET /api/me
│   │   │   └── Tasks/                # TaskItem (domain), EF config, DTOs + validation, TaskService, /api/tasks endpoints
│   │   └── Infrastructure/
│   │       ├── Auth/                 # Firebase JwtBearer setup (FIREBASE_PROJECT_ID; FIREBASE_AUTH_EMULATOR_HOST, Development only)
│   │       ├── Data/                 # AppDbContext (+ owner query filter), IOwnedEntity, design-time factory, Migrations/
│   │       ├── Errors/               # ProblemDetails setup
│   │       ├── Logging/              # JSON logging: console or LOG_FILE (NReco.Logging.File), LOG_LEVEL
│   │       └── Web/                  # Static React build from wwwroot + SPA fallback
│   └── Quests.Tests/                 # xUnit tests (ApiFactory, TestTokens, smoke, auth, auth emulator, tasks, static web, file logging)
├── frontend/                         # React + TypeScript (Vite), Pico CSS, oxlint
│   ├── firebase.json, .firebaserc    # Firebase Auth Emulator config (npm run emulators)
│   ├── .env.emulator                 # VITE_USE_AUTH_EMULATOR=1 for npm run dev:emulator
│   └── src/                          # main.tsx, App.tsx, index.css
│       ├── features/auth/            # Firebase client (+ emulator hookup), useAuth, AuthGate (sign-in UI)
│       ├── features/tasks/           # tasksApi, dateTime helpers, TaskForm, TaskRow, TasksPage
│       └── shared/api.ts             # Fetch wrapper (apiGet/apiSend) with Firebase ID token
├── deploy/                           # compose.yml (api + Caddy) and Caddyfile, uploaded to the instance by CI
├── docs/                             # Detailed documentation (local-testing.md, deployment.md)
├── Dockerfile                        # Production image: API + React build in wwwroot
├── .dockerignore
├── .github/workflows/ci.yml          # CI: backend build/test, frontend lint/build, deploy on push to main
├── .config/dotnet-tools.json         # Local tools (dotnet-ef)
├── .ai-factory/                      # AI Factory context: config, description, architecture, rules, plans
├── .claude/skills/, .agents/skills/  # aif-* skills
├── .mcp.json                         # MCP servers
├── README.md
└── AGENTS.md                         # This file
```

## Key Entry Points

| File | Purpose |
|------|---------|
| backend/Quests.Api/Program.cs | API composition root (logging, errors, data, auth, endpoints) |
| backend/Quests.Tests/ApiFactory.cs | In-memory API host for tests (temp SQLite, test JWT key) |
| frontend/src/main.tsx | Frontend entry point |
| frontend/vite.config.ts | Vite config, `/api` proxy to `localhost:5080` |
| .github/workflows/ci.yml | CI workflow + deploy job |
| Dockerfile | Production image build |
| deploy/compose.yml | Production Compose stack |
| .ai-factory/DESCRIPTION.md | Project specification, features, stack, open decisions |
| .ai-factory/config.yaml | AI Factory configuration |
| .mcp.json | MCP server configuration |

## Documentation

| Document | Path | Description |
|----------|------|-------------|
| README | README.md | Setup, run, tests, config, auth, tasks API, migrations |
| Local Testing | docs/local-testing.md | Firebase Auth Emulator: fake sign-in, Playwright |
| Deployment | docs/deployment.md | Lightsail setup, GitHub secrets, operations, rollback |

## AI Context Files

| File | Purpose |
|------|---------|
| AGENTS.md | Project structure map for AI agents (this file) |
| .ai-factory/DESCRIPTION.md | Project specification and tech stack |
| .ai-factory/ARCHITECTURE.md | Architecture pattern, folder structure, dependency rules |
| .ai-factory/rules/base.md | Coding conventions |

## Agent Rules

- **Ask before deciding.** The user wants to make decisions: ask about features, design choices, and trade-offs instead of choosing silently. Do not invent functionality — suggest it and wait for approval.
- **Save tokens.** The user has a small AI budget: prefer simple, conventional solutions, read only the files needed, and avoid unnecessary exploration or verbose output.
- **No task dependencies.** Organization is limited to grouping/hierarchy and tags.
- Never combine shell commands with `&&`, `||`, or `;` — run each command as a separate call.
  - Wrong: `git checkout main && git pull`
  - Right: First `git checkout main`, then `git pull origin main`
