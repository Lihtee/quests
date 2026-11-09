# Project Base Rules

> No code exists yet — these are initial conventions derived from the chosen stack (.NET + React/TypeScript). Update them once real code establishes patterns.

## Naming Conventions

- C# files: PascalCase, one public type per file (`TaskItem.cs`)
- C# types, methods, properties: PascalCase; locals and parameters: camelCase; private fields: `_camelCase`; interfaces: `I` prefix
- TypeScript/React component files: PascalCase (`TaskList.tsx`); other TS files: camelCase (`syncEngine.ts`)
- TS variables and functions: camelCase; types/interfaces and React components: PascalCase
- Database tables/columns: follow EF Core defaults (PascalCase)

## Module Structure

- Backend (.NET) and frontend (React + Capacitor) live in separate top-level folders; exact layout is defined in `.ai-factory/ARCHITECTURE.md`.
- Domain logic (time windows, recurrence, "What to do now" selection) is kept free of framework and I/O code so it can be unit-tested.
- All data access is scoped by the authenticated user id.

## Error Handling

- Backend: return RFC 7807 ProblemDetails for errors; validate input at the API boundary; no exceptions for expected control flow.
- Frontend: sync and network failures must never lose local changes — keep them queued and retry.

## Control Flow

- Prefer flat, readable control flow over deeply nested conditionals. Use guard clauses, early `return`/`continue`, small named helper methods, or explicit classification logic when they make the code easier to follow. Handle edge cases and irrelevant branches early so the main path stays visible.

## Logging

- Backend: `Microsoft.Extensions.Logging` with structured message templates; level configurable via `LOG_LEVEL`. Never log tokens or personal task content at Information level or above.
- Frontend: minimal `console` logging in development only.

## Testing

- Backend: xUnit for domain logic and API.
- Frontend: Vitest for sync and domain logic.
