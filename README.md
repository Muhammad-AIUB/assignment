# School Admission System — Backend API

A first-come-first-served school admission API. A student **holds** a seat in a class, then **confirms** it before the
hold expires, or the seat is released for the next student. Built with .NET 8 (controllers + service layer), EF Core on
PostgreSQL, FluentValidation and Serilog.

| | |
|---|---|
| **API base URL** | <http://localhost:8080> |
| **Swagger UI** (interactive docs for every endpoint) | <http://localhost:8080/swagger> |
| **Health check** | <http://localhost:8080/health> |

## Run it with Docker Compose

You need Docker with Compose v2. Nothing else (no .NET SDK, no PostgreSQL install).

```bash
docker compose up --build
```

The first run builds the API image and starts PostgreSQL 16; the database schema is created automatically when the API
starts. When both containers are healthy, open <http://localhost:8080/swagger>.

```bash
docker compose down        # stop; the database is kept in a Docker volume
docker compose down -v     # stop and delete the database
```

PostgreSQL is not published to the host (only the API needs it). To look inside it:
`docker compose exec postgres psql -U postgres -d school_admission`.

### Settings

Everything has a default, so no configuration is needed. To change something, copy `.env.example` to `.env` (it is
git-ignored) and edit it; Docker Compose picks it up on the next `up`.

| Variable | Default | Meaning |
|---|---|---|
| `HOLD_DURATION_SECONDS` | `600` | How long a hold lasts before the seat is released (must be > 0) |
| `HOLD_EXPIRY_SWEEP_ENABLED` | `true` | Background job that tidies expired holds. Holds expire correctly even when it is off |
| `HOLD_EXPIRY_SWEEP_INTERVAL_SECONDS` | `5` | How often that job runs |
| `API_PORT` | `8080` | Host port the API is published on |
| `POSTGRES_DB` / `POSTGRES_USER` / `POSTGRES_PASSWORD` | `school_admission` / `postgres` / `postgres` | Database credentials. Only applied when the volume is first created; after changing them run `docker compose down -v` |

## Try it

A complete flow with `curl` (replace the ids with the ones your responses return):

```bash
# 1. create a class with 2 seats                                     -> 201 { "id": "c_...", ... }
curl -X POST http://localhost:8080/classes -H "Content-Type: application/json" \
     -d '{"name":"Class 6 - Morning Shift","totalSeats":2}'

# 2. a student holds a seat (17-digit registration number)           -> 201 { "holdId": "h_...", "status": "HELD", "expiresAt": ... }
curl -X POST http://localhost:8080/classes/<classId>/holds -H "X-Student-Id: 20152691234567890"

# 3. the same student confirms before expiresAt                      -> 200 { "status": "CONFIRMED" }
curl -X POST http://localhost:8080/holds/<holdId>/confirm -H "X-Student-Id: 20152691234567890"

# 4. seat counts and the student's own holds
curl http://localhost:8080/classes/<classId>
curl http://localhost:8080/me/holds -H "X-Student-Id: 20152691234567890"
```

Holding twice as the same student returns `409 DUPLICATE_ACTIVE_HOLD`; a missing or malformed `X-Student-Id` returns
`400 INVALID_STUDENT_ID`. Set `HOLD_DURATION_SECONDS=30` in `.env` to watch a hold expire without waiting ten minutes.

## Endpoints

Student identity is the `X-Student-Id` header: exactly 17 digits. Every error is `{ "error": "SOME_CODE", "message": "..." }`.
The full request/response shapes are in [`docs/API_CONTRACT.md`](docs/API_CONTRACT.md) and in Swagger UI.

| # | Endpoint | Header | Success | Errors (`error` code) |
|---|---|---|---|---|
| 1 | `POST /classes` | | 201 | 400 `VALIDATION_ERROR` |
| 2 | `GET /classes` | | 200 | |
| 3 | `GET /classes/{classId}` | | 200 | 404 `CLASS_NOT_FOUND` |
| 4 | `POST /classes/{classId}/holds` | `X-Student-Id` | 201 | 400 `INVALID_STUDENT_ID`, 404 `CLASS_NOT_FOUND`, 409 `NO_SEAT_AVAILABLE` / `DUPLICATE_ACTIVE_HOLD` |
| 5 | `GET /holds/{holdId}` | | 200 | 404 `HOLD_NOT_FOUND` |
| 6 | `POST /holds/{holdId}/confirm` | `X-Student-Id` | 200 (repeat confirm by the owner is also 200) | 400, 404, 409 `NOT_HOLD_OWNER` / `HOLD_EXPIRED` / `HOLD_CANCELLED` |
| 7 | `DELETE /holds/{holdId}` | `X-Student-Id` | 204 | 400, 404, 409 `NOT_HOLD_OWNER` / `HOLD_EXPIRED` / `HOLD_CANCELLED` / `HOLD_ALREADY_CONFIRMED` |
| 8 | `GET /me/holds` | `X-Student-Id` | 200 | 400 |

`GET /health` is an extra operational probe (200, or 503 `DATABASE_UNAVAILABLE`); it is not part of the student contract.

## How it works, in short

- **No overselling.** A seat is claimed with one atomic SQL `UPDATE` that checks and takes the seat in a single step, and the
  database itself refuses any state with more seats taken than exist. This holds with many simultaneous requests and with
  several API processes sharing one database.
- **One active hold per student per class**, enforced by the service and by a unique index in the database.
- **Holds expire on time.** A held seat past `expiresAt` is reported as `EXPIRED` and counts as free immediately, computed from
  the stored expiry time; the background job only tidies the stored rows. Nothing important lives in the API's memory, so
  restarting the API loses nothing.
- **Errors are never HTTP 500** for a predictable failure; even unexpected exceptions are answered with a 4xx in the same shape.

Design decisions are logged in [`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md); choices the brief left open are listed in
[`ASSUMPTIONS.md`](ASSUMPTIONS.md).

## Run the tests

You need the .NET 8 SDK and a running Docker. The integration tests start their own throwaway PostgreSQL 16 containers
(Testcontainers), so there is no database to install, and they do not touch the Compose stack: both can run at the same
time. The first run pulls the `postgres:16` image.

```bash
dotnet test                          # everything
dotnet test tests/UnitTests          # unit tests only; no Docker needed
dotnet test tests/IntegrationTests   # integration tests against real PostgreSQL
```

Useful subsets of the integration tests:

```bash
dotnet test tests/IntegrationTests --filter "FullyQualifiedName~SeatRace"            # concurrency: 50 parallel requests for 5 seats, in one process and across two
dotnet test tests/IntegrationTests --filter "FullyQualifiedName~RestartPersistence"  # kill the API, start a new one, state is unchanged
dotnet test tests/IntegrationTests --filter "FullyQualifiedName~SwaggerDocument"     # the published OpenAPI document matches the contract
```

The whole suite takes about 2 to 4 minutes (the first run is slower: it builds and pulls the image). The concurrency and
restart tests start real API processes and are the slowest.

## Run without Docker (optional)

Needs the .NET 8 SDK and a PostgreSQL reachable at `localhost:5432` with database `school_admission` and user/password
`postgres`/`postgres` (the Development settings in `src/Api/appsettings.Development.json`). For example:

```bash
docker run -d --name school-pg -p 5432:5432 -e POSTGRES_DB=school_admission -e POSTGRES_PASSWORD=postgres postgres:16-alpine
dotnet run --project src/Api
```

The API then listens on <http://localhost:8080> (the `http` launch profile) and applies the migrations on startup. Override the
connection with the `ConnectionStrings__Postgres` environment variable.

## Project layout

```
src/
  Api/             controllers, DTOs, services, middleware, background expiry job, Program.cs, Dockerfile
  Domain/          entities and the hold-status rule
  Infrastructure/  DbContext, EF configuration, migrations, options
tests/
  UnitTests/         no database
  IntegrationTests/  real PostgreSQL via Testcontainers
docs/
  API_CONTRACT.md    the frozen request/response contract
  ARCHITECTURE.md    design decisions, session by session
  PROGRESS.md        build log
docker-compose.yml   PostgreSQL + API
ASSUMPTIONS.md       decisions the brief did not specify
```
