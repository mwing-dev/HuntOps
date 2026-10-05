# HuntOps

> Hunting applications, permits, points, deadlines, and season operations.

HuntOps is a self-hosted, generic platform for tracking hunting draw applications, preference-point windows, OTC and leftover sales, draw results, reporting deadlines and season dates. It works across any number of jurisdictions, agencies, species, years and permit systems, and it pushes reminders to your phone through [ntfy](https://ntfy.sh).

The approved design lives in **[docs/design/v1-architecture.md](docs/design/v1-architecture.md)**.

## Status

| Phase | Scope | State |
|---|---|---|
| 1 | Solution scaffold, Docker Compose, PostgreSQL, migrations, health checks, logging, CI | ✅ Done |
| 2 | Domain model + EF Core + REST API + API keys | ✅ Done |
| 3 | Dashboard, owner sign-in, settings, API-key management | ✅ Done |
| 4 | Reminder rules, worker scheduling, ntfy | ⏳ Next |
| 5 | Action completion + notification history | — |
| 6 | CSV/JSON/ICS imports + review queue | — |
| 7 | Exact-URL sources, text extraction, change detection | — |
| 8 | Gemini structured extraction | — |
| 9 | MCP server | — |
| 10 | Hardening + full documentation | — |

What runs today:
- the full container stack (PostgreSQL, one-shot migrator, web host, worker host, ntfy)
- the generic domain model:
  - jurisdictions, agencies and programs (including umbrella programs)
  - an extensible event-type catalog
  - events, with timezone-aware point-in-time and window semantics
  - required actions, with append-only status history
- a REST API (`/api`) with OpenAPI and Swagger UI, protected by hashed API keys with read/write scopes
- a **responsive dashboard** (MudBlazor; desktop and phone):
  - **Action required** shows what needs doing, with one-click *Mark completed* / *Not applicable* / *Reopen* and visible history
  - upcoming events, and recently completed or missed actions
  - pages for jurisdictions, agencies, programs (with season-by-season history), events and actions, plus a calendar and agenda view
  - event types, preferences (time zone, quiet hours), API keys, and system health
- owner sign-in (ASP.NET Core Identity), with a single owner bootstrapped from `.env`
- `apikey` and `dev-seed` CLI commands
- liveness and readiness endpoints, the worker heartbeat, Data Protection keys in PostgreSQL, and structured JSON logging

Reminders (Phase 4) and later features are not built yet. The **Sources** menu item is shown disabled until Phase 7.

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
  HuntOps.Api/             REST endpoints, API-key auth, problem-details errors, OpenAPI
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
# edit .env: set POSTGRES_PASSWORD, HUNTOPS_ADMIN_EMAIL and HUNTOPS_ADMIN_PASSWORD (see "Owner account" below)
docker compose up -d --build --wait
```

- Dashboard: <http://127.0.0.1:8080> (sign in with the owner account)
- Health: <http://127.0.0.1:8080/health> (liveness) · <http://127.0.0.1:8080/health/ready> (readiness)
- ntfy: <http://127.0.0.1:8081>

`--wait` returns once every service reports healthy. `docker compose ps` shows the state, and `docker compose logs -f huntops-web huntops-worker` follows the logs.

Published ports bind to `127.0.0.1` by default, so put a reverse proxy in front of them. Proxy configuration is documented in Phase 10.

## Environment variables

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
| `HUNTOPS_SWAGGER_ENABLED` | `true` | Serve `/openapi/v1.json` and Swagger UI at `/swagger`. The document is public; calling the API always needs a key. |
| `HUNTOPS_ADMIN_EMAIL` / `HUNTOPS_ADMIN_PASSWORD` | *(empty)* | Create the owner account **once**, only when no owner exists. Password: 12+ characters with upper, lower, digit and symbol. Never overwrites an existing account. |
| `HUNTOPS_OWNER_TIMEZONE` | `America/Los_Angeles` | Initial owner time zone; afterwards edit it under Settings → Preferences |
| `HUNTOPS_COOKIE_SECURE` | `auto` | `auto`: cookies are Secure whenever the request is HTTPS (directly or via a trusted proxy), so `http://127.0.0.1` works locally. `always`: HTTPS required. |
| `HUNTOPS_TRUST_FORWARDED_HEADERS` | `false` | Trust `X-Forwarded-For/Proto/Host` from your reverse proxy |
| `HUNTOPS_GIT_COMMIT` | *(empty)* | Optional build identifier shown on the System page |
| `NTFY_BASE_URL` | `http://huntops-ntfy` | Internal ntfy URL. Phase 3 only uses it for the System page's connectivity check. |

`.env.example` also lists the variables for later phases, commented out. The full reference is §9.1 of the architecture doc. **Never commit `.env`.**

## Owner account & dashboard

HuntOps V1 has a single **owner** account (ASP.NET Core Identity). Registration does not exist.

**Bootstrap.** On every `docker compose up`, the one-shot `huntops-migrate` container applies migrations and then runs the owner bootstrap:

| Situation | What happens |
|---|---|
| No users exist, and `HUNTOPS_ADMIN_EMAIL` / `HUNTOPS_ADMIN_PASSWORD` are set | The owner is created. The password must meet the policy; otherwise the migrate container exits with code 1 and a message describing the rule, never the value. |
| No users exist, and the variables are not set | Nothing is created. **HuntOps never creates default credentials.** The login page explains what to set. |
| An owner already exists | The variables are ignored and never change the password. You can remove `HUNTOPS_ADMIN_PASSWORD` from `.env`. |

**Sign-in security:**
- Lockout after 5 failed attempts, for 15 minutes.
- Antiforgery tokens on the login and logout forms.
- The auth cookie is `HttpOnly` and `SameSite=Lax`, and Secure per `HUNTOPS_COOKIE_SECURE`. It slides over 14 days, or lasts only for the browser session unless "keep me signed in" is ticked.
- Open sessions are re-validated every 30 minutes.
- Data Protection keys live in PostgreSQL, so restarts keep you signed in.
- The browser session never authorizes `/api`; the API always needs an API key.

**Behind a TLS reverse proxy (production):** set `HUNTOPS_TRUST_FORWARDED_HEADERS=true` and `HUNTOPS_COOKIE_SECURE=always`. The proxy must pass WebSockets through, because the dashboard is Blazor Server.

**Placeholder owner migration (Phase 2 → 3).** Phase 2 stored action history and API keys under the placeholder user id `"owner"`. The first bootstrap that has an owner re-assigns those rows, in one transaction:
- `api_keys.user_id` and `action_status_changes.user_id` change from `owner` to the owner's Identity id.
- Nothing else changes. History rows keep their id, sequence, status, outcome, note, time, channel and `changed_by`.
- The append-only trigger permits exactly this one update. Any other `UPDATE`, `DELETE` or `TRUNCATE` of history is still rejected.

The step is idempotent, and it also adopts keys created with the CLI before an owner existed.

### Development data

```bash
docker compose exec huntops-worker dotnet HuntOps.Worker.dll dev-seed
```

This creates sample data through the application services:
- Kansas · KDWP · Resident Antelope, with 2023–2024 "Applied – not drawn" and 2025–2026 "Preference point purchased"
- an upcoming 2027 application and a 2027 draw-results date
- a currently open deer-permit window, so the dashboard has something to act on

It is idempotent, **development data only**, and never runs automatically. It is not part of any migration.

## REST API

The API lives under `/api`.
- Every endpoint requires an API key. Read-only keys can call `GET`, and everything else needs a `write` key.
- Unknown routes, validation failures and server errors all return RFC 9457 `application/problem+json`, with no stack traces.
- **Reference:** Swagger UI at <http://127.0.0.1:8080/swagger>, OpenAPI document at `/openapi/v1.json`.

### API keys

Keys look like `hops_<8-char id>_<secret>`. Only a SHA-256 hash is stored, and the plaintext is shown once, at creation.

**Dashboard:** go to **Settings → API keys** to create (read or write, optional expiry) or revoke keys.

**CLI:** for recovery or admin use, the same operations are available from the worker:

```bash
docker compose exec huntops-worker dotnet HuntOps.Worker.dll apikey create --name claude-code --scope write
```

```bash
docker compose exec huntops-worker dotnet HuntOps.Worker.dll apikey create --name phone --scope read --expires-days 365
```

```bash
docker compose exec huntops-worker dotnet HuntOps.Worker.dll apikey list
```

```bash
docker compose exec huntops-worker dotnet HuntOps.Worker.dll apikey revoke hops_ab12cd34
```

`docker compose exec` output is not captured in container logs. Send the key as `Authorization: Bearer <key>` (or `X-Api-Key: <key>`). `GET /api/me` shows which key you're using.

### Conventions

| Topic | Rule |
|---|---|
| Dates / times | `yyyy-MM-dd` and 24-hour `HH:mm`. Time zones are IANA ids (`America/Chicago`), and instants are ISO-8601 UTC. |
| Event schedule | `startDate` is required. Add `endDate` for a window, or leave it out for a point-in-time event. An end date without an `endTime` means **the end of that day** in the event's zone, so "closes June 12" is 23:59:59.999 local. `timeZoneId` defaults to the jurisdiction's. Skipped or repeated DST times resolve leniently. |
| Point events | Their actions become `open` once they start and are never auto-`missed`. Model deadlines as windows. |
| Action status | Stored decisions are `completed`, `notApplicable`, `cancelled` and `reopened`, and they're append-only. `upcoming`, `open` and `missed` are derived from the clock. |
| Updates | `PUT` replaces the resource and requires the `version` you last read. A stale version returns `409`. Changing an event's dates clears verification unless you send `"verified": true`. |
| Deletes | `DELETE` archives (soft-deletes) and `POST …/restore` undoes it. History is never removed. A jurisdiction or agency with active children can't be archived. |
| Uniqueness | One active event per program, season year, event type and qualifier. Use `qualifier` for "Phase 2", "Round 1" and similar. |

### Examples

```bash
export HUNTOPS_KEY=hops_...           # a write key
H="Authorization: Bearer $HUNTOPS_KEY"

curl -s -H "$H" -H 'Content-Type: application/json' http://127.0.0.1:8080/api/jurisdictions \
  -d '{"name":"Kansas","code":"KS","country":"US","timeZoneId":"America/Chicago"}'

curl -s -H "$H" -H 'Content-Type: application/json' http://127.0.0.1:8080/api/agencies \
  -d '{"jurisdictionId":"<jurisdictionId>","name":"Kansas Department of Wildlife and Parks","abbreviation":"KDWP"}'

curl -s -H "$H" -H 'Content-Type: application/json' http://127.0.0.1:8080/api/programs \
  -d '{"agencyId":"<agencyId>","name":"Resident Antelope","species":"Antelope","residency":"Resident"}'

curl -s -H "$H" -H 'Content-Type: application/json' http://127.0.0.1:8080/api/events \
  -d '{"programId":"<programId>","eventTypeKey":"application-period","seasonYear":2027,
       "startDate":"2027-05-12","endDate":"2027-06-12",
       "actions":[{"title":"Apply or buy preference point"}]}'

curl -s -H "$H" "http://127.0.0.1:8080/api/action-items?days=60"     # what needs attention
curl -s -H "$H" "http://127.0.0.1:8080/api/events/upcoming?days=90"

curl -s -H "$H" -H 'Content-Type: application/json' \
  http://127.0.0.1:8080/api/events/<eventId>/complete -d '{"outcome":"Preference point purchased"}'
```

Validation errors look like this:

```json
{
  "title": "One or more validation errors occurred.",
  "status": 400,
  "errors": {
    "startDate": ["'2027-13-01' is not a valid date; use yyyy-MM-dd (e.g. 2027-06-12)."],
    "timeZoneId": ["'Kansas Time' is not a known IANA time zone (e.g. America/Chicago)."]
  },
  "traceId": "00-…"
}
```

### Endpoint overview

| Area | Endpoints |
|---|---|
| Jurisdictions / agencies / programs | `GET`, `POST /api/{jurisdictions,agencies,programs}` · `GET`, `PUT`, `DELETE /{id}` · `POST /{id}/restore` · `GET /api/programs/{id}/history` |
| Event types | `GET`, `POST /api/event-types` · `GET`, `PUT`, `DELETE /api/event-types/{key}` · `POST /{key}/restore`. 10 generic types are seeded. |
| Events | `GET /api/events` (filters, paging) · `GET /api/events/upcoming` · `GET`, `PUT`, `DELETE /api/events/{id}` · `POST /{id}/restore`, `/verify`, `/complete` · `GET`, `POST /{id}/actions` |
| Actions | `GET`, `PUT`, `DELETE /api/actions/{id}` · `POST /{id}/restore` · `POST /{id}/status` · `GET /{id}/history` · `GET /api/action-items` |
| Access | `GET /api/me` |

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

| Migration | Phase | Contents |
|---|---|---|
| `InitialCreate` | 1 | `worker_heartbeats`, `data_protection_keys` |
| `DomainModelAndApiKeys` | 2 | Reference data, event types (seeded), events, actions, `action_status_changes`, `api_keys`. Includes check constraints, filtered unique indexes, and an append-only trigger that rejects `UPDATE`, `DELETE` and `TRUNCATE` on action history. |
| `IdentityAndOwnerSettings` | 3 | Identity tables (`users`, `roles`, `user_*`, `role_claims`), a single-owner index, and `owner_settings`. It also narrows the history trigger to allow the one-time placeholder-owner adoption. |

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

They cover:
- the REST API and authentication, sign-in, lockout and antiforgery, through `WebApplicationFactory` over HTTPS
- the owner bootstrap and placeholder migration
- dashboard components via [bUnit](https://bunit.dev): rendered against the real services and database, asserting behavior (text, saved data) rather than MudBlazor markup

## Backup and restore (preview)

Data lives in two named volumes: `huntops-pgdata` and `huntops-ntfy-data`. The minimal backup until Phase 10 adds scripts:

```bash
docker exec huntops-postgres pg_dump -U huntops -d huntops -Fc > huntops-$(date +%F).dump
```

## Contributing workflow

- Each phase is developed on `phase-N-<slug>` and merged through one PR.
- The PR includes build and test results, a `docker compose` smoke-test result, and documentation updates.
