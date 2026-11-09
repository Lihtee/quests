# Project Roadmap

> Personal task manager (web + Android) with time-window tasks, "What to do now", phone notifications and offline-first sync.

**Rule:** every milestone after Foundation ends deployed, visible in the UI, testable by hand, and usable for real.

## Milestones

### Foundation

- [x] **Project skeleton** — .NET 10 API (Minimal APIs, feature folders), React+Vite app, EF Core + SQLite, ProblemDetails, structured logging, GitHub Actions CI
- [x] **Auth & accounts** — Firebase Google sign-in, API validates the JWT, email allowlist, all data scoped by user id
- [ ] **Deployment** — single service on Lightsail serving API + static web over HTTPS, env-var secrets, CI deploys on merge; shows a signed-in "hello" page

### Features

- [ ] **Minimal tasks** — web UI to create/edit/complete/delete tasks with title, notes and a single due date/time; list sorted by due date (online-only for now)
- [ ] **Offline-first sync** — Dexie local store, pending-change queue, delta sync, conflict resolution; testable: edit offline, reconnect, see changes on another device after focus/refresh
- [ ] **Real-time updates** — SignalR "doorbell"; testable: edit in one tab/device, the other updates within seconds without refresh
- [ ] **Android app** — Capacitor wrapper of the React app, sign-in on device, works offline; installable APK produced by CI
- [ ] **Notifications** — reminder at the task's due time via exact local alarms + FCM high-priority push fallback for tasks created on another device
- [ ] **Time windows** — hard deadline, soft deadline, desirable period replace the single due date; list shows window state; notifications follow the new fields (rules decided during planning)
- [ ] **Recurring tasks** — recurrence rules; each occurrence gets its own deadlines and window
- [ ] **"What to do now"** — the button and its selection algorithm
- [ ] **Organization** — grouping/hierarchy and tags
- [ ] **Backups** — off-instance SQLite backups of the production database

## Completed

| Milestone | Date |
|-----------|------|
| Project skeleton | 2026-11-09 |
| Auth & accounts | 2026-11-09 |
