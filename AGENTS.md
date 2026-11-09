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

No application code exists yet. Current contents:

```
quests/
├── .ai-factory/          # AI Factory context: config, description, architecture, rules, plans
│   ├── config.yaml       # AI Factory settings (language, paths, workflow, git)
│   ├── DESCRIPTION.md    # Project specification and tech stack
│   └── rules/base.md     # Project coding conventions
├── .claude/skills/       # Built-in aif-* skills for Claude Code
├── .agents/skills/       # Agent skills (mirror for other agents)
├── .mcp.json             # MCP servers: github, filesystem, chromeDevtools, playwright
└── AGENTS.md             # This file
```

## Key Entry Points

| File | Purpose |
|------|---------|
| .ai-factory/DESCRIPTION.md | Project specification, features, stack, open decisions |
| .ai-factory/config.yaml | AI Factory configuration |
| .mcp.json | MCP server configuration |

## Documentation

| Document | Path | Description |
|----------|------|-------------|
| README | README.md | Not created yet |

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
