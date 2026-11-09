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

Project skeleton (no feature code yet). Detailed target layout: `.ai-factory/ARCHITECTURE.md`.

```
quests/
├── backend/
│   ├── Quests.slnx                   # .NET solution
│   ├── nuget.config                  # Restricts NuGet sources to nuget.org
│   ├── Quests.Api/                   # ASP.NET Core Minimal API
│   │   ├── Program.cs                # Composition root
│   │   └── Infrastructure/
│   │       ├── Data/                 # AppDbContext, DataSetup, Migrations/
│   │       ├── Errors/               # ProblemDetails setup
│   │       └── Logging/              # JSON console logging, LOG_LEVEL
│   └── Quests.Tests/                 # xUnit tests (ApiFactory + smoke tests)
├── frontend/                         # React + TypeScript (Vite), oxlint
│   └── src/                          # main.tsx, App.tsx
├── .github/workflows/ci.yml          # CI: backend build/test, frontend lint/build
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
| backend/Quests.Api/Program.cs | API composition root (logging, errors, data, health check) |
| backend/Quests.Tests/ApiFactory.cs | In-memory API host for tests (temp SQLite) |
| frontend/src/main.tsx | Frontend entry point |
| frontend/vite.config.ts | Vite config, `/api` proxy to `localhost:5080` |
| .github/workflows/ci.yml | CI workflow |
| .ai-factory/DESCRIPTION.md | Project specification, features, stack, open decisions |
| .ai-factory/config.yaml | AI Factory configuration |
| .mcp.json | MCP server configuration |

## Documentation

| Document | Path | Description |
|----------|------|-------------|
| README | README.md | Setup, run, tests, config, migrations |

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
