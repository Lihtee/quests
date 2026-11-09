# Architecture: Structured Modules (Simplified, Modern .NET)

## Overview

The backend is a single ASP.NET Core service organized into **feature modules** (Tasks, Sync, Notifications, Identity). Each module keeps its endpoints, services, models, and DTOs together in one folder. It follows modern .NET practice: **Minimal APIs** instead of controllers, **EF Core `DbContext` used directly** (no repository layer — `DbContext` already is the unit of work/repository), records for DTOs, nullable reference types, and ProblemDetails for errors.

Business rules — time windows, recurrence, "What to do now" — live in **rich models and small pure domain classes**, not in endpoints, so they are easy to unit test. The React app mirrors this with feature folders. Chosen because it is simple for one developer and low on boilerplate (and AI tokens), but still keeps the core rules isolated and testable.

## Decision Rationale

- **Project type:** personal task manager, web + Android (Capacitor), offline-first sync, single small deployment
- **Tech stack:** .NET 10, ASP.NET Core Minimal APIs + SignalR, EF Core + SQLite; React + TypeScript (Vite), Dexie, Capacitor
- **Key factor:** user asked for "simple, but modern practices"; one developer, moderate domain logic, low token budget

## Folder Structure

```
quests/
├── backend/
│   ├── Quests.Api/
│   │   ├── Program.cs                      # Composition root: DI, auth, endpoint mapping
│   │   ├── Features/
│   │   │   ├── Tasks/
│   │   │   │   ├── TaskItem.cs             # Rich model: invariants, Complete(), Reschedule()
│   │   │   │   ├── TimeWindow.cs           # Value object: hard/soft deadline, desirable period
│   │   │   │   ├── Recurrence.cs           # Value object: recurrence rule + next occurrence
│   │   │   │   ├── NextTaskSelector.cs     # Pure "What to do now" ranking (no I/O)
│   │   │   │   ├── TaskEndpoints.cs        # MapGroup("/api/tasks") — thin handlers
│   │   │   │   ├── TaskService.cs          # Use-case orchestration: load → model method → save
│   │   │   │   └── TaskDtos.cs             # Request/response records
│   │   │   ├── Sync/
│   │   │   │   ├── SyncEndpoints.cs        # POST /api/sync (push changes, pull since version)
│   │   │   │   ├── SyncService.cs          # Change versioning, conflict resolution
│   │   │   │   └── SyncHub.cs              # SignalR "doorbell": notifies a user's clients to sync
│   │   │   ├── Notifications/
│   │   │   │   ├── DeviceToken.cs          # FCM token per device
│   │   │   │   ├── ReminderWorker.cs       # BackgroundService: sends due FCM pushes
│   │   │   │   └── FcmSender.cs            # Firebase Admin SDK wrapper
│   │   │   └── Identity/
│   │   │       ├── CurrentUser.cs          # Resolves user id from Firebase JWT claims
│   │   │       └── AllowlistAuthorization.cs
│   │   └── Infrastructure/
│   │       ├── Data/AppDbContext.cs        # EF Core DbContext + entity configurations
│   │       ├── Data/Migrations/
│   │       ├── Auth/FirebaseJwtSetup.cs    # JwtBearer config for Firebase tokens
│   │       └── Errors/ProblemDetailsSetup.cs
│   └── Quests.Tests/                       # xUnit: domain rules first, then API tests
│       └── Features/Tasks/...
├── frontend/
│   ├── src/
│   │   ├── main.tsx / App.tsx
│   │   ├── features/
│   │   │   ├── tasks/                      # Task list, editor, groups/tags UI + hooks
│   │   │   ├── now/                        # "What to do now" screen
│   │   │   ├── sync/                       # Sync engine: outbox, delta pull, SignalR client
│   │   │   ├── notifications/              # Local notifications scheduling, FCM registration
│   │   │   └── auth/                       # Firebase Auth sign-in
│   │   ├── domain/                         # Pure TS rules shared by features (no React, no I/O)
│   │   └── shared/
│   │       ├── db.ts                       # Dexie schema (local offline copy + outbox)
│   │       ├── api.ts                      # Fetch wrapper with auth token
│   │       └── ui/                         # Reusable UI components
│   ├── android/                            # Capacitor-generated Android project
│   └── capacitor.config.ts
└── .github/workflows/                      # CI/CD to Lightsail
```

## Dependency Rules

- ✅ `Endpoints → Services → Models / AppDbContext`
- ✅ Features → `Infrastructure/` (DbContext, auth, error setup)
- ✅ A feature may call another feature's **service** (e.g. Tasks → Sync to bump change version)
- ✅ Frontend: `features/* → domain/, shared/`
- ❌ Models (`TaskItem`, `TimeWindow`, `Recurrence`, `NextTaskSelector`) importing EF Core, ASP.NET, HTTP, or clock/time APIs — pass `now` in as a parameter
- ❌ Endpoints containing business rules or querying `DbContext` for anything beyond trivial reads
- ❌ Frontend `domain/` importing React, Dexie, Capacitor, or `fetch`
- ❌ Frontend components writing to the server directly — all writes go to Dexie + outbox; the sync engine talks to the server

## Layer/Module Communication

- **Client → server writes:** UI writes to Dexie and appends to an outbox; the sync engine pushes the outbox via `POST /api/sync` and pulls changes since the last known version.
- **Server → clients:** after a sync commit, `SyncHub` sends a "changed" signal to the user's other connections; they run delta sync. Signals carry no data.
- **Reminders:** clients schedule local notifications from local data; `ReminderWorker` sends FCM pushes for due reminders as a fallback.
- **Cross-module (backend):** direct service calls via DI; no event bus.

## Key Principles

1. **Rules in models, orchestration in services.** `TaskItem` enforces its own invariants (e.g. soft deadline ≤ hard deadline); services only load, call model methods, and save.
2. **Time is an input.** Domain code takes `DateTimeOffset now` (backend `TimeProvider` at the edge) so time-window and recurrence logic is deterministic and testable.
3. **Every query is scoped by user id.** Use an EF Core global query filter on the owner id; never trust ids from the client without the filter.
4. **Offline-first is the default path.** The server is the sync authority, not the source for UI rendering; the UI always reads from Dexie.
5. **Keep it boring.** No MediatR, no repository layer, no CQRS, no extra projects until a real need appears — ask the user before adding abstractions.

## Open Architecture Decision (ask the user during planning)

**Where do recurrence expansion and "What to do now" logic live?** They are needed offline on the phone (and for local notifications), and the server needs reminder times for FCM. To avoid writing the same rules twice:
- *Option A (recommended):* the rules live in the TypeScript `domain/`. Clients generate occurrences with deterministic ids (`seriesId + date`) so devices don't create duplicates, and they sync computed reminder times to the server. The server only stores data and sends FCM at stored times.
- *Option B:* the rules live in C# on the server. Clients show what was last synced, and offline creation of recurring tasks is limited.

## Code Examples

### Rich model with time passed in (C#)

```csharp
public sealed class TaskItem
{
    public Guid Id { get; private set; }
    public Guid OwnerId { get; private set; }
    public string Title { get; private set; } = "";
    public TimeWindow Window { get; private set; } = TimeWindow.None;
    public DateTimeOffset? CompletedAt { get; private set; }

    public void Complete(DateTimeOffset now)
    {
        if (CompletedAt is not null) return;
        CompletedAt = now;
    }

    public bool IsInDesirablePeriod(DateTimeOffset now) =>
        CompletedAt is null && Window.DesirableContains(now);
}

public sealed record TimeWindow(
    DateTimeOffset? DesirableFrom,
    DateTimeOffset? SoftDeadline,
    DateTimeOffset? HardDeadline)
{
    public static readonly TimeWindow None = new(null, null, null);

    public bool DesirableContains(DateTimeOffset now) =>
        DesirableFrom is { } from && now >= from && (SoftDeadline is null || now <= SoftDeadline);
}
```

### Thin Minimal API endpoint → service (C#)

```csharp
public static class TaskEndpoints
{
    public static RouteGroupBuilder MapTaskEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/tasks").RequireAuthorization();
        group.MapPost("/{id:guid}/complete", async (Guid id, TaskService tasks, CancellationToken ct) =>
            await tasks.CompleteAsync(id, ct) ? TypedResults.NoContent() : Results.NotFound());
        return group;
    }
}

public sealed class TaskService(AppDbContext db, TimeProvider clock)
{
    public async Task<bool> CompleteAsync(Guid id, CancellationToken ct)
    {
        var task = await db.Tasks.FindAsync([id], ct); // owner filter applied globally
        if (task is null) return false;

        task.Complete(clock.GetUtcNow());
        await db.SaveChangesAsync(ct);
        return true;
    }
}
```

### UI writes locally, sync engine sends (TypeScript)

```ts
// features/tasks/completeTask.ts
import { db } from '../../shared/db';

export async function completeTask(id: string, now = new Date()) {
  await db.transaction('rw', db.tasks, db.outbox, async () => {
    await db.tasks.update(id, { completedAt: now.toISOString() });
    await db.outbox.add({ entity: 'task', id, op: 'update', fields: ['completedAt'], at: now.toISOString() });
  });
  // the sync engine picks up the outbox; no fetch here
}
```

## Anti-Patterns

- ❌ Business rules in endpoint lambdas or React components
- ❌ Calling `DateTime.Now` / `new Date()` inside domain logic (inject or pass `now`)
- ❌ Queries without the owner filter (`IgnoreQueryFilters()` only with a clear reason)
- ❌ UI reading from the API instead of Dexie, or writing to the server and skipping the outbox
- ❌ Keeping SignalR or timers alive in the background on the phone (battery)
- ❌ Adding layers/packages (MediatR, AutoMapper, repositories) without asking the user
- ❌ Task-to-task dependencies (explicit non-goal)
