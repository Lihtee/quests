# Implementation Plan: Deployment

Branch: none (git.create_branches=false)
Created: 2026-11-09

## Original Request
deployment

## Settings
- Testing: yes (small)
- Logging: verbose; inside Docker logs go to a rotating file, not stdout
- Docs: yes
- Commit strategy: incremental
- Development methodology: implementation-first

## Roadmap Linkage
Milestone: "Deployment"
Rationale: The user named this roadmap milestone as the planning request.

## Decisions (confirmed by user)
- **Runtime:** Docker, run by Docker Compose on the existing Lightsail instance (Docker is already installed and running).
- **Image:** one image = ASP.NET Core API + React build in `wwwroot`. Built by CI, pushed to a **public Docker Hub** repo using GitHub secrets `DOCKERHUB_USERNAME` + `DOCKERHUB_TOKEN` (a Docker Hub access token is recommended; the account password also works). The instance pulls without login.
- **HTTPS:** **Caddy** container (official `caddy:2-alpine` image) in front of the API, automatic Let's Encrypt certificate, HTTP→HTTPS redirect. The API port is not published to the host.
- **Hostname:** free **DuckDNS** subdomain (`<name>.duckdns.org`) pointed once, by hand, at the instance's static IP. Changing to a real domain later = change the `DOMAIN` variable.
- **Logging in Docker:** `NReco.Logging.File` provider. When `LOG_FILE` is set, logs are written as JSON lines to that file with size rotation and console logging is turned off. Code keeps verbose Debug logs; level is still set by `LOG_LEVEL`.
- **Auto-initialization:** the CI deploy job creates the folder on the instance, uploads `compose.yml` + `Caddyfile`, writes `.env` from GitHub secrets/variables **on every deploy**, then runs `docker compose pull` + `up -d`. No manual steps on the box beyond prerequisites.
- **Deploy trigger:** push to `main` (merge), only after the `backend` and `frontend` CI jobs pass.
- **Backups:** **not in this milestone.** The user wants them scheduled after all existing milestones (see Requirements Reconciliation).

Defaults chosen by the plan (conventional, no new features):
- Data in Docker named volumes: `quests-data` (`/app/data/quests.db`), `quests-logs` (`/app/logs`), `caddy-data`, `caddy-config`. Named volumes avoid host file-permission issues with the non-root `app` user of the .NET image.
- Docker `json-file` log rotation (`max-size: 10m`, `max-file: 3`) on both services, so Caddy's stdout logs cannot fill the disk.
- Server secrets reach the container only through `.env` → `environment:`; nothing secret is committed or baked into the image (Firebase web config is public by design).
- SSH host key is pinned via a `LIGHTSAIL_KNOWN_HOSTS` secret (no blind `ssh-keyscan` in CI).
- `index.html` is served with `Cache-Control: no-cache` so browsers pick up new deploys; Vite's hashed assets keep default caching.

Out of scope: backups; SignalR/WebSocket specifics (Caddy proxies WebSockets by default, nothing to do now); Android build; staging environment.

## Requirements Reconciliation
Authority: user decisions in this session override ROADMAP wording where they differ. ROADMAP (Deployment), DESCRIPTION (Architecture Notes: single deployable backend; NFR: Security, Logging, Cost) and ARCHITECTURE (`.github/workflows/` = CI/CD to Lightsail) otherwise agree.

| Requirement | Source | Resolution | Verification |
|---|---|---|---|
| Single service on Lightsail serving API + static web | ROADMAP, DESCRIPTION | One API container serves `/api/*` and the React build; Caddy only terminates TLS and proxies (refinement, not a second app service) | `StaticWebTests`; manual check on deployed URL |
| Over HTTPS / "HTTPS only" | ROADMAP, DESCRIPTION NFR | Caddy with Let's Encrypt; HTTP redirects to HTTPS; API port not exposed | Manual: `http://` redirects, `https://` valid cert |
| Env-var secrets, never committed | ROADMAP, DESCRIPTION NFR | GitHub secrets/variables → `.env` on instance (written each deploy, mode 600) → container env | Review: no secrets in repo/image; manual check of `.env` permissions |
| Off-instance SQLite backups | ROADMAP | **Conflict → resolved by user:** removed from this milestone; to be a new milestone after all existing ones. Roadmap is owned by `/aif-roadmap` — run it to add "Backups" at the end and drop backups from the Deployment line | — |
| CI deploys on merge | ROADMAP | `deploy` job on push to `main`, `needs: [backend, frontend]` | First merge deploys; CI health check passes |
| Shows a signed-in "hello" page | ROADMAP | Existing AuthGate UI, served from the deployed site; DuckDNS domain added to Firebase authorized domains | Manual: sign in on deployed URL → "Hello, <email>" |
| Logging configurable via `LOG_LEVEL`, structured | DESCRIPTION NFR | Unchanged; plus `LOG_FILE` switches output to a JSON-lines file (user: not stdout in Docker) | `FileLoggingTests`; manual `docker compose exec api tail /app/logs/quests.log` |

Request routing combinations (static web × API × auth):

| Request | Token | Expected | Verification |
|---|---|---|---|
| `GET /`, `GET /some/client/route` | none | 200 `index.html`, `Cache-Control: no-cache` | StaticWebTests |
| `GET /assets/<file>` (exists) | none | 200 file | StaticWebTests |
| `GET /api/health` | none | 200 | existing AuthTests / StaticWebTests |
| `GET /api/me` | valid allowlisted | 200 JSON (unchanged) | existing AuthTests |
| `GET /api/does-not-exist` | none | 401 ProblemDetails (never `index.html`) | StaticWebTests |
| `GET /api/does-not-exist` | valid allowlisted | 404 ProblemDetails (never `index.html`) | StaticWebTests |

Meaning preservation: PASS

## Tasks

### Phase 1: Backend — serve the web app and file logging

- [x] Task 1: Serve the React build and SPA fallback from the API
  - Files: `backend/Quests.Api/Infrastructure/Web/StaticWebSetup.cs` (new), `backend/Quests.Api/Program.cs`
  - `UseQuestsStaticWeb()`: `UseDefaultFiles()` + `UseStaticFiles()` placed **before** `UseAuthentication`/`UseAuthorization` (files are public; the authorization fallback policy must not block them). `OnPrepareResponse`: set `Cache-Control: no-cache` for `index.html`.
  - `MapQuestsStaticWeb()` (after other endpoint mappings): `MapFallback("/api/{**path}", () => Results.NotFound())` — keeps the default (authorized) policy, so unknown API paths give 401 without a token and 404 ProblemDetails with one; then `MapFallbackToFile("index.html").AllowAnonymous()` with the same `no-cache` header. Unknown `/api/*` must never return `index.html`.
  - Missing `wwwroot` (local dev) must not break startup: the API still runs, fallback returns 404.
  - Logging: Information at startup "Static web root {WebRootPath}, index.html present: {HasIndex}"; Warning when `index.html` is missing outside Development; Debug "SPA fallback served index.html for {Path}".

- [x] Task 2: Optional file logging via `LOG_FILE` (NReco.Logging.File)
  - Files: `backend/Quests.Api/Quests.Api.csproj`, `backend/Quests.Api/Infrastructure/Logging/LoggingSetup.cs`, `backend/Quests.Api/Infrastructure/Logging/LoggingSettings.cs`
  - Add package `NReco.Logging.File` (latest stable 1.x; check nuget.org at implementation time).
  - `LoggingSettings` gets `string? LogFile`. In `AddQuestsLogging`: if `LOG_FILE` is empty → current JSON console (unchanged). If set → **no console provider**; `AddFile(path, o => { Append = true; FileSizeLimitBytes = 10 MB; MaxRollingFiles = 5; FormatLogEntry = JSON line })`. JSON line fields: `Timestamp` (UTC, same format as console), `LogLevel`, `Category`, `EventId`, `Message`, `Exception` (when present), written with `Utf8JsonWriter` (one line per entry). Create the directory if missing.
  - Same `LOG_LEVEL` minimum level for both outputs.
  - Logging: Information "Logging to file {LogFile}" (written to the file); Debug effective level (existing).

- [x] Task 3: Tests for static web and file logging (depends on 1, 2)
  - Files: `backend/Quests.Tests/ApiFactory.cs`, `backend/Quests.Tests/Infrastructure/StaticWebTests.cs` (new), `backend/Quests.Tests/Infrastructure/FileLoggingTests.cs` (new)
  - `ApiFactory`: optional temp web root (create a temp dir with `index.html` and `assets/app.js`, `builder.UseWebRoot(dir)`), deleted on dispose; optional extra settings (for `LOG_FILE`). Keep existing tests unchanged.
  - `StaticWebTests`: every row of the request routing table above (content type `text/html` and `no-cache` for `index.html`; ProblemDetails content type for 401/404 on `/api/*`).
  - `FileLoggingTests`: with `LOG_FILE=<temp>/logs/test.log`, start the host, call `/api/health`, dispose; assert the file exists and every line parses as JSON with `LogLevel` and `Message`.
  - Logging: none in tests.

### Phase 2: Docker image, Compose and CI deploy

- [x] Task 4: Dockerfile and .dockerignore (depends on 1, 2)
  - Files: `Dockerfile` (repo root), `.dockerignore`
  - Stage `web` (`node:24-alpine`): `ARG VITE_FIREBASE_API_KEY`, `VITE_FIREBASE_AUTH_DOMAIN`, `VITE_FIREBASE_PROJECT_ID`, `VITE_FIREBASE_APP_ID` → `ENV`; `npm ci`, `npm run build` in `frontend/`.
  - Stage `api` (`mcr.microsoft.com/dotnet/sdk:10.0`): copy `backend/nuget.config` + csproj first for restore caching, then `dotnet publish backend/Quests.Api -c Release -o /out` (Tests project not included).
  - Final (`mcr.microsoft.com/dotnet/aspnet:10.0`): copy `/out` → `/app`, `web` dist → `/app/wwwroot`; create `/app/data`, `/app/logs` owned by `$APP_UID`; `USER $APP_UID`; defaults `ASPNETCORE_HTTP_PORTS=8080`, `ConnectionStrings__Default=Data Source=/app/data/quests.db`, `LOG_FILE=/app/logs/quests.log`; `ENTRYPOINT ["dotnet", "Quests.Api.dll"]`.
  - `.dockerignore`: `**/bin`, `**/obj`, `**/node_modules`, `frontend/dist`, `*.db*`, `**/.env*`, `**/*.local`, `.git`, `.ai-factory`, `.claude`, `.agents`.
  - Logging: n/a (build output only). Manual check: `docker build` locally with the 4 build args, `docker run` with `FIREBASE_PROJECT_ID`/`ALLOWED_EMAILS`, open `http://localhost:8080`.

- [x] Task 5: Compose stack and Caddy config (depends on 4)
  - Files: `deploy/compose.yml`, `deploy/Caddyfile`
  - `compose.yml` services:
    - `api`: `image: ${IMAGE}:${IMAGE_TAG}`, `restart: unless-stopped`, env `FIREBASE_PROJECT_ID`, `ALLOWED_EMAILS`, `LOG_LEVEL` (default `Information`) from `.env`; volumes `quests-data:/app/data`, `quests-logs:/app/logs`; no published ports.
    - `caddy`: `caddy:2-alpine`, `restart: unless-stopped`, ports `80:80`, `443:443`, `443:443/udp`; env `DOMAIN`; volumes `./Caddyfile:/etc/caddy/Caddyfile:ro`, `caddy-data:/data`, `caddy-config:/config`; `depends_on: [api]`.
    - Both: `logging: { driver: json-file, options: { max-size: 10m, max-file: "3" } }`.
  - `Caddyfile`: `{$DOMAIN} { encode zstd gzip; reverse_proxy api:8080 }`.
  - Logging: Caddy access logs stay off (default); errors go to Caddy's stdout (rotated).

- [x] Task 6: CI build, push and deploy job (depends on 4, 5)
  - Files: `.github/workflows/ci.yml`
  - New job `deploy`: `needs: [backend, frontend]`, `if: github.event_name == 'push' && github.ref == 'refs/heads/main'`, `concurrency: { group: deploy, cancel-in-progress: false }`, `permissions: contents: read`.
  - Steps:
    1. `actions/checkout`; `docker/setup-buildx-action`; `docker/login-action` with `secrets.DOCKERHUB_USERNAME` / `secrets.DOCKERHUB_TOKEN`.
    2. `docker/build-push-action`: push `${{ secrets.DOCKERHUB_USERNAME }}/quests:${{ github.sha }}` and `:latest`; build args from `vars.VITE_FIREBASE_API_KEY`, `vars.VITE_FIREBASE_AUTH_DOMAIN`, `vars.VITE_FIREBASE_PROJECT_ID`, `vars.VITE_FIREBASE_APP_ID`; GitHub Actions cache (`type=gha`).
    3. SSH setup: write `secrets.LIGHTSAIL_SSH_KEY` to `~/.ssh/id_deploy` (mode 600) and `secrets.LIGHTSAIL_KNOWN_HOSTS` to `~/.ssh/known_hosts`.
    4. Build `.env` on the runner with `umask 077`: `DOMAIN=${{ vars.DOMAIN }}`, `IMAGE=<user>/quests`, `IMAGE_TAG=${{ github.sha }}`, `FIREBASE_PROJECT_ID=${{ vars.VITE_FIREBASE_PROJECT_ID }}`, `ALLOWED_EMAILS=${{ secrets.ALLOWED_EMAILS }}`, `LOG_LEVEL=${{ vars.LOG_LEVEL || 'Information' }}`.
    5. Auto-init + deploy: `ssh` → `mkdir -p ~/quests`; `scp deploy/compose.yml deploy/Caddyfile .env` → `~/quests/`; `ssh` → `chmod 600 ~/quests/.env`, check `docker compose version` (fail with a clear message if missing), `docker compose -f ~/quests/compose.yml --project-directory ~/quests pull`, `up -d --remove-orphans`, `docker image prune -f`.
    6. Health check: poll `https://${{ vars.DOMAIN }}/api/health` up to ~2 minutes (first run waits for the certificate); fail the job if it never returns 200, and print `docker compose ps` + last 50 lines of `docker compose logs caddy` over SSH.
  - Never echo secrets; no `set -x` in steps that touch them.
  - Logging: `echo "::group::"` sections with short step messages (image tag, host, health check attempts).

### Phase 3: Documentation

- [ ] Task 7: Deployment runbook (`/aif-docs` checkpoint) (depends on 1–6)
  - Files: `docs/deployment.md` (new), `README.md`, `AGENTS.md`
  - `docs/deployment.md`:
    - One-time prerequisites: Lightsail static IP; firewall open TCP 80, 443 (+ UDP 443); SSH user in the `docker` group and Compose plugin installed; DuckDNS subdomain → static IP; Firebase console → Authorized domains: add the DuckDNS domain; Docker Hub public repo `quests` + access token.
    - GitHub configuration table — secrets: `DOCKERHUB_USERNAME`, `DOCKERHUB_TOKEN`, `LIGHTSAIL_HOST`, `LIGHTSAIL_USER`, `LIGHTSAIL_SSH_KEY`, `LIGHTSAIL_KNOWN_HOSTS` (from `ssh-keyscan <ip>` run once from a trusted machine), `ALLOWED_EMAILS`; variables: `DOMAIN`, `VITE_FIREBASE_*`, optional `LOG_LEVEL`.
    - What the first successful CI run does (auto-init), what each deploy does, how to change `ALLOWED_EMAILS` (update secret → re-run deploy job).
    - Operations: view logs (`docker compose exec api tail -f /app/logs/quests.log`), restart, rollback (re-run the `deploy` job of an older workflow run — it deploys that run's `IMAGE_TAG`).
    - Note: backups are not set up yet (planned as a later milestone).
  - README: short "Deployment" section linking to `docs/deployment.md`; add `LOG_FILE` to the configuration table.
  - AGENTS.md: add `Dockerfile`, `.dockerignore`, `deploy/`, `docs/`, `Infrastructure/Web/` to the structure map.

## Commit Plan
- **Commit 1** (after tasks 1-3): "feat: serve web app from API and add optional file logging"
- **Commit 2** (after tasks 4-6): "ci: build Docker image and deploy to Lightsail with Caddy"
- **Commit 3** (after task 7): "docs: add deployment runbook"
