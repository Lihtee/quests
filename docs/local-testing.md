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

## See Also

- [README](../README.md): Quick Start, configuration, authentication
- [Deployment](deployment.md): production setup on Lightsail
