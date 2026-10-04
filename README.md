# HuntOps

> Hunting applications, permits, points, deadlines, and season operations.

HuntOps is a self-hosted, generic platform for tracking hunting draw applications, preference-point windows, OTC and leftover sales, draw results, reporting deadlines and season dates. It works across any number of jurisdictions, agencies, species, years and permit systems, and it pushes reminders to your phone through [ntfy](https://ntfy.sh).

The approved design lives in **[docs/design/v1-architecture.md](docs/design/v1-architecture.md)**.

## Status

| Phase | Scope | State |
|---|---|---|
| 1 | Solution scaffold, Docker Compose, PostgreSQL, migrations, health checks, logging, CI | ✅ Done |
| 2 | Domain model + EF Core + REST API | ⏳ Next |
| 3 | Blazor dashboard + authentication | — |
| 4 | Reminder rules, worker scheduling, ntfy | — |
| 5 | Action completion + notification history | — |
| 6 | CSV/JSON/ICS imports + review queue | — |
| 7 | Exact-URL sources, text extraction, change detection | — |
| 8 | Gemini structured extraction | — |
| 9 | MCP server | — |
| 10 | Hardening + full documentation | — |

What runs today:
- the full container stack (PostgreSQL, one-shot migrator, web host, worker host, ntfy)
- liveness and readiness endpoints
- a worker heartbeat stored in the database
- Data Protection keys persisted in PostgreSQL
- structured JSON logging

The dashboard, API and reminders arrive in later phases.

## Architecture (summary)

```
huntops-web      Blazor dashboard + REST API (/api) + MCP (/mcp)   → HuntOps.Web host
huntops-worker   schedulers, source pipeline, jobs, heartbeat       → HuntOps.Worker host
huntops-migrate  one-shot EF Core migrations, then exits            → HuntOps.Worker `migrate`
huntops-postgres PostgreSQL 17                                       → volume huntops-pgdata
huntops-ntfy     self-hosted ntfy push server                        → volume huntops-ntfy-data
```

```
src/
  HuntOps.Domain/          entities and domain rules (no EF, no ASP.NET)
  HuntOps.Application/     services and abstractions (IHuntOpsDb, ...); all business logic lives here
  HuntOps.Infrastructure/  EF Core + PostgreSQL (NodaTime, snake_case), migrations, health, logging
  HuntOps.Api/             REST endpoint library (Phase 2)
  HuntOps.Mcp/             MCP tool library (Phase 9)
  HuntOps.Web/             host: Blazor + Api + Mcp + /health
  HuntOps.Worker/          host: background services + /health (internal) + `migrate` command
tests/
  HuntOps.UnitTests/
  HuntOps.IntegrationTests/  real PostgreSQL via Testcontainers
```

## Prerequisites

- Docker with Compose v2 (Docker Desktop on Windows or macOS, or Docker Engine on Linux)
- For development: .NET SDK 10.0.100 or newer (`global.json` rolls forward to the latest 10.0 feature band)
- For integration tests: Docker running (Testcontainers starts `postgres:17-alpine`)

## Quick start (Docker)

```bash
cp .env.example .env
# edit .env: set POSTGRES_PASSWORD (required), e.g. `openssl rand -base64 32`
docker compose up -d --build --wait
```

- Dashboard: <http://127.0.0.1:8080>
- Health: <http://127.0.0.1:8080/health> (liveness) · <http://127.0.0.1:8080/health/ready> (readiness)
- ntfy: <http://127.0.0.1:8081>

`--wait` returns once every service reports healthy. `docker compose ps` shows the state, and `docker compose logs -f huntops-web huntops-worker` follows the logs.

Published ports bind to `127.0.0.1` by default, so put a reverse proxy in front of them. Proxy configuration is documented in Phase 10.

## Environment variables (Phase 1)

| Variable | Default | Purpose |
|---|---|---|
| `POSTGRES_PASSWORD` | **required** | Database password, used by postgres and the .NET services |
| `POSTGRES_DB` / `POSTGRES_USER` | `huntops` / `huntops` | Database name and user |
| `HUNTOPS_BIND_ADDRESS` | `127.0.0.1` | Host interface for published ports |
| `HUNTOPS_WEB_PORT` / `NTFY_HOST_PORT` | `8080` / `8081` | Published host ports |
| `HUNTOPS_PUBLIC_URL` | *(empty)* | Public dashboard URL (used for notification links from Phase 4) |
| `HUNTOPS_WORKER_ID` | `worker` | Stable worker identity for heartbeats |
| `LOG_LEVEL` | `Information` | `Trace`, `Debug`, `Information`, `Warning`, `Error` or `Critical` |
| `LOG_FORMAT` | `json` | `json` (structured) or `text` (human-readable) |
| `NTFY_PUBLIC_URL` | `http://localhost:8081` | The ntfy server's public base URL (becomes its `NTFY_BASE_URL`) |
| `NTFY_UPSTREAM_BASE_URL` | `https://ntfy.sh` | Upstream relay for instant iOS delivery |
| `NTFY_AUTH_DEFAULT_ACCESS` | `deny-all` | ntfy default ACL |
| `NTFY_IMAGE_TAG` | `v2.28.0` | Pinned ntfy image tag |

`.env.example` also lists the variables for later phases, commented out. The full reference is §9.1 of the architecture doc. **Never commit `.env`.**

## Running locally (without containers for the .NET apps)

```bash
# PostgreSQL only, published on 127.0.0.1:5432
docker compose -f docker-compose.yml -f docker-compose.dev.yml up -d huntops-postgres
```

Point the apps at it with either `POSTGRES_PASSWORD` (plus optional `POSTGRES_HOST`, `POSTGRES_PORT`, `POSTGRES_DB`, `POSTGRES_USER`) or a full `ConnectionStrings__HuntOps`. Then:

```bash
dotnet run --project src/HuntOps.Worker -- migrate
dotnet run --project src/HuntOps.Web
dotnet run --project src/HuntOps.Worker
```

The web host listens on <http://localhost:5117> and the worker's health endpoint on <http://localhost:5118/health/ready>. The launch profiles use `LOG_FORMAT=text`.

## Database migrations

Migrations live in `src/HuntOps.Infrastructure/Persistence/Migrations` and use the `dotnet-ef` tool (pinned in `dotnet-tools.json`; run `dotnet tool restore`).

**Create a migration** (no database connection needed):

```bash
dotnet ef migrations add <Name> --project src/HuntOps.Infrastructure --startup-project src/HuntOps.Infrastructure --output-dir Persistence/Migrations
```

**Apply migrations.** In Docker this happens automatically: the one-shot `huntops-migrate` service runs on every `docker compose up`, applies pending migrations, and exits. `huntops-web` and `huntops-worker` start only after it succeeds. It never drops or recreates anything.

Manually against a local database:

```bash
dotnet run --project src/HuntOps.Worker -- migrate
```

Or with the EF tool, which reads the full connection string from `HUNTOPS_DESIGN_CONNECTION` (there is no built-in default credential):

```bash
dotnet ef database update --project src/HuntOps.Infrastructure --startup-project src/HuntOps.Infrastructure
```

**Development reset.** ⚠️ This permanently deletes all HuntOps data and ntfy state.

```bash
docker compose down -v
docker compose up -d --build --wait
```

## Health checks

| Endpoint | Meaning |
|---|---|
| `GET /health` | Liveness: the process is up. Runs no dependency checks. |
| `GET /health/ready` | Readiness: the database is reachable **and** every migration is applied. The worker also requires a recent heartbeat. Returns `503` otherwise. |

Responses are JSON with the status, duration and description of each check. Exception details are never exposed. Each .NET container is its own Docker healthcheck (`dotnet HuntOps.Web.dll --healthcheck`), so the runtime images don't need `curl`. The worker serves its endpoints on internal port 8081, which isn't published.

## Logging

Every service writes structured JSON (Serilog compact format) to stdout, with an `Application` property: `huntops-web`, `huntops-worker` or `huntops-migrate`. Set `LOG_FORMAT=text` for human-readable output. HuntOps never logs connection strings, passwords or tokens.

## Tests

```bash
dotnet test --solution HuntOps.slnx
```

Tests use xUnit v3 on Microsoft.Testing.Platform, enabled in `global.json`. Integration tests start a disposable PostgreSQL container, so Docker must be running.

## Backup and restore (preview)

Data lives in two named volumes: `huntops-pgdata` and `huntops-ntfy-data`. The minimal backup until Phase 10 adds scripts:

```bash
docker exec huntops-postgres pg_dump -U huntops -d huntops -Fc > huntops-$(date +%F).dump
```

## Contributing workflow

- Each phase is developed on `phase-N-<slug>` and merged through one PR.
- The PR includes build and test results, a `docker compose` smoke-test result, and documentation updates.
