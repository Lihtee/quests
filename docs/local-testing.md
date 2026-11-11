[Back to README](../README.md) · [Deployment →](deployment.md)

# Local Testing with the Auth Emulator

Run the whole app locally and sign in with fake Google accounts, without a real Google login. This also works for browser automation (Playwright). It uses the [Firebase Auth Emulator](https://firebase.google.com/docs/emulator-suite/connect_auth) from `firebase-tools` (a frontend devDependency).

## Prerequisites

- Everything from the README's Quick Start, including `frontend/.env.local`
- **Java 21** (JDK or JRE), which the Firebase emulators need

## Start It

Use three terminals:

```bash
# 1. Auth Emulator on 127.0.0.1:9099 (project from frontend/.firebaserc)
cd frontend
npm run emulators
```

```bash
# 2. API in emulator mode, with fake accounts allowlisted
ALLOWED_EMAILS="alice@example.com,bob@example.com" \
  dotnet run --project backend/Quests.Api --launch-profile emulator
```

```bash
# 3. Frontend in emulator mode
cd frontend
npm run dev:emulator
```

Open the app and click **Sign in with Google**. In the emulator popup, choose **Add new account**, enter an allowlisted email (for example `alice@example.com`), and sign in. The page shows a "Running in emulator mode" banner.

Emulator accounts live only in the emulator's memory and are lost when it stops. Tasks are stored in the normal local database. To keep test data separate, also set `ConnectionStrings__Default="Data Source=e2e.db"` for the API.

## How It Works

| Part | Setting | Effect |
|------|---------|--------|
| Emulator | `frontend/firebase.json`, `frontend/.firebaserc` | Auth only, port 9099, project id `quests-86cfd` |
| Frontend | `frontend/.env.emulator` (`VITE_USE_AUTH_EMULATOR=1`), loaded by `vite --mode emulator` | Firebase Auth connects to `http://127.0.0.1:9099` (dev server only) |
| API | Launch profile `emulator` sets `FIREBASE_AUTH_EMULATOR_HOST=127.0.0.1:9099` | Accepts the emulator's **unsigned** ID tokens. Issuer, audience and expiry are still checked, and a warning is logged at startup |

**Safety:** `FIREBASE_AUTH_EMULATOR_HOST` is allowed only when the environment is `Development`. In any other environment the API refuses to start, so production never accepts unsigned tokens. Never set it on a server.

## Offline Sync Check (Playwright)

Use two browser contexts signed in as the same account (A and B) and one context with a second account. Go offline with `context.setOffline(true)`. To make the app sync at once after reconnecting, call `setOffline(false)` and fire the `online` event (`window.dispatchEvent(new Event('online'))`), or focus the window.

| Check | Steps | Expected |
|-------|-------|----------|
| Offline writes | A goes offline, then creates, edits, completes and deletes tasks. A reconnects. | A's changes show on B after focus or a reload. |
| Conflict | A goes offline and edits a task's title. B completes the same task. A reconnects. | Both changes are kept: new title, completed. |
| Delete vs. edit | A goes offline and deletes a task. B edits the task. A reconnects. | The task is back on both devices, with B's edit. |
| Isolation | Sign in with the second account. | None of these tasks are visible. |

While offline, the browser console shows `[sync] failed { status: undefined }`, and the change stays in the IndexedDB `outbox` table of `quests-<uid>` until a sync succeeds.

## Real-Time Check

Open the app in two tabs or windows (A and B) signed in as the same account. Keep both visible (for example, side by side).

| Check | Steps | Expected |
|-------|-------|----------|
| Live update | Create, edit, complete or delete a task in A. | B shows the change within a few seconds, without focus or a reload. |
| Hidden tab | Switch B to another tab. Edit a task in A. Switch back to B. | B updates as soon as it is visible (catch-up sync on reconnect). |
| No echo | Edit a task in A. | In DEV, A's console shows no `[signal] changed` for its own change; B's does. |

The Vite dev server proxies the hub's WebSocket (`/api` with `ws: true`).

## See Also

- [README](../README.md): Quick Start, configuration, authentication
- [Deployment](deployment.md): production setup on Lightsail
- [Android App](android.md): APK build and on-device test checklist
