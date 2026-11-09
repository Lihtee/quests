[Back to README](../README.md)

# Deployment

Quests runs on one AWS Lightsail instance as a Docker Compose stack:

| Service | Image | Role |
|---------|-------|------|
| `api` | `<DOCKERHUB_USERNAME>/quests:<commit sha>` | ASP.NET Core API + React build (one container, port 8080, not published to the host) |
| `caddy` | `caddy:2-alpine` | HTTPS with an automatic Let's Encrypt certificate, HTTP → HTTPS redirect, reverse proxy to `api:8080` |

Every push to `main` deploys automatically, after the `backend` and `frontend` CI jobs pass.

## One-Time Prerequisites

1. **Lightsail instance** with Docker and the Compose plugin (`docker compose version` works).
2. **Static IP** attached to the instance.
3. **Firewall** (Lightsail → Networking): open TCP 80, TCP 443 and UDP 443.
4. **SSH user** for deploys in the `docker` group (`sudo usermod -aG docker <user>`, then log in again).
5. **DuckDNS subdomain** (`<name>.duckdns.org`) pointed at the static IP.
6. **Firebase console → Authentication → Settings → Authorized domains:** add the DuckDNS domain.
7. **Docker Hub:** a public repository named `quests` and an access token (Account settings → Personal access tokens, read & write).

## GitHub Configuration

Repository → Settings → Secrets and variables → Actions.

**Secrets:**

| Name | Value |
|------|-------|
| `DOCKERHUB_USERNAME` | Docker Hub user name (the image is `<username>/quests`) |
| `DOCKERHUB_TOKEN` | Docker Hub access token (the account password also works) |
| `LIGHTSAIL_HOST` | Static IP (or hostname) of the instance |
| `LIGHTSAIL_USER` | SSH user, e.g. `ubuntu` |
| `LIGHTSAIL_SSH_KEY` | Private SSH key for that user (full PEM text) |
| `LIGHTSAIL_KNOWN_HOSTS` | Output of `ssh-keyscan <static ip>`, run once from a trusted machine. Pins the host key; CI never trusts an unknown host. |
| `ALLOWED_EMAILS` | Comma-separated emails allowed to sign in |

**Variables:**

| Name | Value |
|------|-------|
| `DOMAIN` | `<name>.duckdns.org` |
| `VITE_FIREBASE_API_KEY` | Firebase web app config |
| `VITE_FIREBASE_AUTH_DOMAIN` | Firebase web app config |
| `VITE_FIREBASE_PROJECT_ID` | Firebase project id (also used as the API's `FIREBASE_PROJECT_ID`) |
| `VITE_FIREBASE_APP_ID` | Firebase web app config |
| `LOG_LEVEL` | Optional, default `Information` |

The Firebase web config is public by design, so it is a variable and is baked into the frontend bundle. Server secrets reach the container only through `~/quests/.env` on the instance; nothing secret is committed or built into the image.

## What a Deploy Does

The `deploy` job in [.github/workflows/ci.yml](../.github/workflows/ci.yml):

1. Builds the image from [Dockerfile](../Dockerfile) and pushes `<user>/quests:<commit sha>` and `:latest` to Docker Hub.
2. Writes `.env` (`DOMAIN`, `IMAGE`, `IMAGE_TAG`, `FIREBASE_PROJECT_ID`, `ALLOWED_EMAILS`, `LOG_LEVEL`) from the secrets and variables.
3. Over SSH: creates `~/quests`, uploads [deploy/compose.yml](../deploy/compose.yml), [deploy/Caddyfile](../deploy/Caddyfile) and `.env` (mode 600).
4. Runs `docker compose pull`, `docker compose up -d --remove-orphans` and `docker image prune -f`.
5. Polls `https://<DOMAIN>/api/health` for up to 2 minutes. On failure it prints `docker compose ps` and the last 50 lines of Caddy's log, then fails the job.

**First run:** steps 3–4 also initialize the instance. Nothing needs to be copied by hand. Caddy obtains the certificate on the first request, so the first health check may take longer.

Deploys never run in parallel (`concurrency: deploy`), and `.env` is rewritten on every deploy.

## Data and Logs

| Docker volume | Mounted at | Contents |
|---------------|------------|----------|
| `quests-data` | `/app/data` | SQLite database `quests.db` |
| `quests-logs` | `/app/logs` | API log `quests.log` (JSON lines, rotated at 10 MB, 5 files) |
| `caddy-data`, `caddy-config` | `/data`, `/config` | Certificates and Caddy state |

Container stdout logs (mainly Caddy) use Docker's `json-file` driver, rotated at 10 MB × 3 files.

> **No backups yet.** The database lives only in the `quests-data` volume on the instance. Off-instance backups are planned as a later milestone.

## Operations

Run on the instance from `~/quests` (Compose picks up `compose.yml` and `.env` there):

| Task | Command |
|------|---------|
| Follow API logs | `docker compose exec api tail -f /app/logs/quests.log` |
| Caddy logs | `docker compose logs --tail 100 caddy` |
| Status | `docker compose ps` |
| Restart | `docker compose restart` |

**Change `ALLOWED_EMAILS`:** update the secret, then re-run the `deploy` job of the latest workflow run on `main`. It rewrites `.env` and recreates the `api` container.

**Rollback:** in GitHub Actions, open an older successful run on `main` and re-run its `deploy` job. It deploys that run's commit (`IMAGE_TAG` = that commit's sha). GitHub allows re-runs for 30 days after a run. The next push to `main` deploys the latest code again.

## See Also

- [README](../README.md) — local development, configuration, authentication
