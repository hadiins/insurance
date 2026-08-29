# دفتر اقساط (Aqsat)

Installment management & collection system for Iranian insurance agencies. See
[`docs/PHASE-1-SPEC.md`](docs/PHASE-1-SPEC.md) for the product spec and [`CLAUDE.md`](CLAUDE.md)
for the non-negotiable engineering rules — read both before changing anything here.

## Stack

ASP.NET Core 8 · EF Core · SQL Server · Hangfire · SignalR · React + Vite + TypeScript. See
`CLAUDE.md` for why these and not the obvious alternatives.

## Local development

```bash
# Backend — needs SQL Server LocalDB (Windows) or a local SQL Server instance
dotnet ef database update --project src/Aqsat.Infrastructure --startup-project src/Aqsat.Api
dotnet run --project src/Aqsat.Api --urls http://localhost:5027

# Frontend — proxies /api and /hubs to :5027 (see src/aqsat-web/vite.config.ts)
cd src/aqsat-web && npm install && npm run dev
```

Run the test suite with `dotnet test tests/Aqsat.UnitTests/Aqsat.UnitTests.csproj` — it exercises
real SQL Server (LocalDB), including Row-Level Security, so it will not pass against an in-memory
provider.

## Deployment

### Build

One image serves both API and frontend — `src/Aqsat.Api/Dockerfile` is a three-stage build: builds
the Vite frontend, publishes the API, then copies the frontend's `dist/` into the API's `wwwroot`
so a single container answers both `/api/*` and everything else (the SPA).

```bash
docker compose -f docker-compose.prod.yml build
```

### Run

```bash
cp .env.example .env   # fill in real values — see the table below
docker compose -f docker-compose.prod.yml --env-file .env up -d
```

**First run only:** the `sqlserver-backup` volume is created empty and owned by `root`, but SQL
Server's own process (`mssql`, uid 10001) needs to write backup files into it — and the `api`
container (running as its unprivileged `app` user since the non-root hardening) needs to delete
expired ones during the retention sweep. Fix ownership/permissions once, right after the first
`up`:

```bash
docker compose -f docker-compose.prod.yml exec -u root sqlserver \
  bash -c "mkdir -p /var/opt/mssql/backup && chown mssql:mssql /var/opt/mssql/backup && chmod 0777 /var/opt/mssql/backup"
```

`sqlserver`'s port is **not** published to the host — only the `api` container can reach it, over
the private compose network. `api` publishes `8080`; put a reverse proxy (TLS termination, your own
domain) in front of it for anything beyond local testing.

### Environment variables

| Variable | Required | Purpose |
|---|---|---|
| `SA_PASSWORD` | yes | SQL Server `sa` password — also becomes the connection string's password |
| `JWT_KEY` | yes | Signs auth tokens. Generate with `openssl rand -base64 48`; rotating it invalidates every active session |
| `NATIONAL_ID_KEY` | yes | AES key encrypting `Customer.NationalId`/`Marketer.NationalId` at rest (CLAUDE.md rule 12). **Losing this key makes every stored national ID permanently unrecoverable** — back it up somewhere other than the DB backup itself |
| `API_IR_KEY` | no | api.ir API key. Leave unset and every call routes to `/api/Sandbox/Echo` (CLAUDE.md's "ask before calling a paid endpoint outside Sandbox") |
| `API_IR_ALLOW_PAID` | no, default `false` | Set `true` only once an agency has a real, funded api.ir key — this is the switch that turns on real per-call billing (SMS, Shahkar, ChequeColor; see `docs/PHASE-1-SPEC.md` §6 for the price list) |
| `BACKUP_RETENTION_DAYS` | no, default `14` | How long `DatabaseBackupJob` keeps old `.bak` files before deleting them |
| `UPDATER_SHARED_TOKEN` | only if using `updater` | Bearer token `Aqsat.Updater` requires on every request (`X-Updater-Token` header) — see "Update service" below |
| `UPDATER_SIGNING_PUBLIC_KEY_PEM` | only if using `updater` | RSA public key (PEM) `Aqsat.Updater` verifies release package signatures against |
| `APP_VERSION` | yes (at build time) | Release version (e.g. `1.0.0`) stamped into the api image when it is BUILT — the update panel reads it from the image itself, never from runtime env. Unstamped images report `unknown` and refuse version-gated packages |
| `DOCKER_GID` | no, default `999` | GID of the host's `/var/run/docker.sock` owning group (`stat -c '%g' /var/run/docker.sock`) — the `updater` image joins this group at build time so it never runs as root. Rebuild with `DOCKER_GID=<gid> docker compose -f docker-compose.prod.yml build updater` if your host differs, or updates fail with "Permission denied" on the socket |

Non-env-var settings worth knowing (in `appsettings.*.json`, overridable via environment):
- `Deployment:KnownProxies` — JSON array of reverse-proxy IPs trusted for X-Forwarded-For/Proto
  (rate limiting and audit IP attribution depend on it; default trusts loopback only)
- `Security:PasswordIterations` — PBKDF2 cost for NEW password hashes (default `600000`; dev/test
  lowers it purely for suite speed). Existing hashes keep their stored count, so lowering it never
  weakens already-hashed passwords

Never commit a real `.env` — it holds the encryption key.

### What starts automatically

On boot, before serving any request, the `api` container:

1. Acquires a SQL Server advisory lock (`sp_getapplock`) so two replicas starting at once can't
   race each other applying migrations, then applies any pending EF Core migrations
   (`Deployment:ApplyMigrationsOnStartup`, default `true` — set `false` in `appsettings.Production.json`
   if you'd rather apply migrations out-of-band before a blue/green cutover).
2. Registers Hangfire's recurring jobs: settlement-deadline recalculation, presence/lock sweep,
   SMS/renewal reminders, and the daily 03:00 database backup.

Both steps are wrapped so a database that isn't reachable yet at startup logs an error and lets the
API keep starting, rather than crashing the whole process (docs/TASKS.md Task 19's hardening pass —
this exact failure mode was found and fixed by testing it directly).

### Backup & restore

Backups run daily and automatically. **Restoring one has an actual tested procedure** —
[`docs/RESTORE-RUNBOOK.md`](docs/RESTORE-RUNBOOK.md) — not just a `BACKUP DATABASE` line and a
hope. Read it before you need it, not during an incident.

## Update service (`Aqsat.Updater`)

**Deferred by default** — `docs/TASKS.md`/`docs/UPDATE-SYSTEM.md` both recommend skipping this
until you're running multiple servers or ~20 customers; a manual `docker compose pull && up -d` is
enough before that. The `updater` service in `docker-compose.prod.yml` only starts if you bring it
up explicitly (`docker compose -f docker-compose.prod.yml up -d updater`) and configure the two
`UPDATER_*` env vars above — omit them and nothing about normal operation changes.

The security model is one strict rule: **`api` never has Docker socket access, only `updater`
does.** They're separate images built from separate Dockerfiles with no shared code — `updater`
doesn't reference any of `Aqsat.Api`'s project files, so there's no path by which a vulnerability in
the internet-facing API could reach the host through it. `updater` has no published port either; it
only accepts requests from `api`, authenticated by `X-Updater-Token`, and only ever executes a
narrow API (`GET /status`, `POST /update`, `POST /rollback`, `GET /progress/{id}`) — never an
arbitrary command, never an arbitrary uploaded file. A package whose signature doesn't verify
against `UPDATER_SIGNING_PUBLIC_KEY_PEM` is rejected outright.

The update panel (`Platform.Owner` + SMS-based 2FA, live SignalR progress, a 60-second maintenance
warning broadcast to every active user, automatic rollback on a failed health check, history) lives
in the main API under `/api/platform/updates` and the frontend's "به‌روزرسانی سیستم" page — visible
only to users whose role carries `Platform.Owner`, which nothing seeds by default in production
(`DevSeeder`'s test fixture grants it purely for exercising these endpoints in tests). It's the only
part of the main app that talks to `updater` at all, over plain HTTP with the same shared token.

`UpdatePackage` rows come from `scripts/release/` — build, sign, and register a release with
`docs/RELEASE-RUNBOOK.md`'s procedure, or in one command on the server itself with
`scripts/release/publish-local.sh` (build → push to a loopback registry → sign → register; the
full single-server walkthrough is [`docs/UPDATE-TEST-RUNBOOK.md`](docs/UPDATE-TEST-RUNBOOK.md)).
You can also hit `POST /api/platform/updates/register` and
`POST /api/platform/updates/packages/{id}/yank` directly, if you're scripting your own pipeline
instead. The signature is checked twice, independently: once by `Aqsat.Api` before a package is
even allowed into the catalog, again by `Aqsat.Updater` immediately before it applies one.

## Data residency

Per `CLAUDE.md`: this product's data must stay hosted in Iran. Whatever host runs
`docker-compose.prod.yml`, and wherever `.bak` files get copied to for offsite storage, that
constraint applies to both.
