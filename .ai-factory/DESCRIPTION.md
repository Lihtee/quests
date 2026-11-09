# Quests — Personal Task Manager

## Overview

A personal task manager (TODO list) available as a web app and an Android app, used by the owner plus a few invited people (each with their own separate task list). Tasks must stay in sync between PC and phone in near real time, work offline, and the phone must notify the user about what to do next.

The distinguishing idea is time-aware tasks: each task can have a **hard deadline**, a **soft deadline**, and a **desirable period** (the window when it is best to act — e.g. pay rent 2–3 days before the deadline, not 30 days before and not on the day). A **"What to do now"** button picks what to work on right now based on these windows.

## Core Features

- **Tasks with time windows** — hard deadline, soft deadline, and desirable action period per task.
- **Recurring tasks** — e.g. "pay the bill on the 1st of every month"; each occurrence carries its own hard/soft deadline and desirable period derived from the recurrence rule.
- **"What to do now"** — a button that returns the task(s) to act on right now. Selection rules: _to be decided with the user_.
- **Phone notifications** — reminders about what to do next (local scheduled alarms + FCM push).
- **Organization** — grouping/hierarchy and tags only. **No task dependencies** (explicit non-goal).
- **Cross-device sync** — offline-first with fast propagation between open devices.
- **Accounts** — owner + a few invited users (email allowlist), separate task lists, no sharing.

> Scope rule: do not add features beyond this list without asking the user first. Suggest features when they seem needed.

## Tech Stack

- **Backend language:** C# / .NET 10 (LTS)
- **Backend framework:** ASP.NET Core Web API + SignalR
- **Frontend:** React + TypeScript (Vite)
- **Mobile:** Capacitor (Android) wrapping the same React app
- **Server database:** SQLite (file on the Lightsail instance)
- **ORM:** EF Core (SQLite provider, migrations)
- **Client offline storage:** IndexedDB via Dexie (same code path on web and Android)
- **Auth:** Firebase Authentication (Google sign-in); API validates Firebase JWTs; invite = email allowlist
- **Notifications:** Capacitor Local Notifications (exact alarms) + Firebase Cloud Messaging (high-priority push)
- **Hosting / CI:** AWS Lightsail (API + web), GitHub, GitHub Actions

## Architecture Notes

- **Offline-first sync:** clients keep a full local copy in IndexedDB and queue local changes. Delta sync ("give me everything changed since version X" + push pending changes) is the **source of truth**; it runs on app start, focus, reconnect, and on a timer. Conflict resolution strategy (e.g. last-write-wins per field) is to be finalized during design.
- **SignalR as a doorbell:** while the app is open, the server notifies connected clients of a user's changes and they immediately run delta sync. Missed signals are harmless — the next delta sync catches up. No sockets or timers are kept alive in the background.
- **Notifications:** the phone schedules exact local alarms for upcoming reminders (work offline and in Doze; requires the Android 12+ "Alarms & reminders" permission). The server sends FCM high-priority push as a fallback for reminders created on another device that the phone hasn't synced yet.
- **Single deployable backend:** one ASP.NET Core service on Lightsail serving the API, the SignalR hub, and the static React build. SQLite file backed up regularly (snapshot/copy off-instance).

## Architecture

See `.ai-factory/ARCHITECTURE.md` for detailed architecture guidelines.
Pattern: Structured Modules (simplified, modern .NET — Minimal APIs, feature folders, EF Core without repositories)

## Non-Functional Requirements

- **Logging:** configurable via `LOG_LEVEL`; structured logs on the backend.
- **Error handling:** structured error responses (RFC 7807 ProblemDetails).
- **Security:** every API/hub call requires a valid Firebase JWT; all data is scoped by user id; email allowlist for access; HTTPS only; secrets via environment variables, never committed.
- **Battery:** no background polling or persistent sockets on the phone; background work is limited to OS-scheduled notifications and FCM.
- **Cost:** low hosting cost (single Lightsail instance, SQLite, Firebase free tier) and low AI-token cost — prefer simple, conventional solutions and a single shared UI codebase.

## Open Decisions (ask the user)

- "What to do now" selection algorithm.
- Recurrence rule model (fixed day of month, every N days/weeks, etc.).
- Conflict resolution details for offline edits.
- UI component library / styling approach.
- Distribution of the Android app (Play Store vs. sideloaded APK).
