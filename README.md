# School Admission System

A first-come-first-served school admission system. A student **holds** a seat in a class, then **confirms** it before the
hold expires, or the seat is released for the next student. The API is .NET 8 (controllers + service layer), EF Core on
PostgreSQL, FluentValidation and Serilog; the web app is React + TypeScript (Vite), served by nginx.

| | |
|---|---|
| **Web app** | <http://localhost:3000> |
| **API base URL** | <http://localhost:8080> |
| **Swagger UI** (interactive docs for every endpoint) | <http://localhost:8080/swagger> |
| **Health check** | <http://localhost:8080/health> |

## Run it with Docker Compose

You need Docker with Compose v2. Nothing else (no .NET SDK, no PostgreSQL install).

```bash
docker compose up --build
```

The first run builds the API and web app images and starts PostgreSQL 16; the database schema is created automatically when
the API starts. The web app starts once the API reports healthy. Then open <http://localhost:3000> (the web app), or
<http://localhost:8080/swagger> (the API's interactive docs). The web app's tests are not run by `up`; see
[Run the frontend tests](#run-the-frontend-tests).

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
| `DB_MAX_POOL_SIZE` | `30` | Most database connections one API instance may hold. PostgreSQL allows 97 ordinary connections, so 30 fits three instances (3 x 30 = 90); lower it, or raise `max_connections`, if you run more. The API applies 30 itself when a connection string sets no `Maximum Pool Size` |
| `CORS_ALLOWED_ORIGINS` | `http://localhost:5173,http://localhost:4173,http://localhost:3000` | Browser origins allowed to call the API, comma-separated (see [Calling the API from a browser](#calling-the-api-from-a-browser-cors)). Empty means none |
| `API_PORT` | `8080` | Host port the API is published on |
| `FRONTEND_PORT` | `3000` | Host port the web app is published on (the default CORS list follows it) |
| `API_BASE_URL` | `http://localhost:<API_PORT>` | Where the **browser** finds the API. The web container writes it into `/config.js` when it starts, so the same image works against any backend with no rebuild. It must be reachable from the browser: never `http://backend:8080` |
| `POSTGRES_DB` / `POSTGRES_USER` / `POSTGRES_PASSWORD` | `school_admission` / `postgres` / `postgres` | Database credentials. Only applied when the volume is first created; after changing them run `docker compose down -v` |

### Calling the API from a browser (CORS)

A web page can only read the API's answers if the API says its origin is allowed. `CORS_ALLOWED_ORIGINS` is that list: each
entry is the scheme, host and port of the page (`http://localhost:5173`), compared exactly.

- **Default:** `http://localhost:5173` (`npm run dev`), `http://localhost:4173` (`npm run preview`) and
  `http://localhost:3000` (the web app container; it follows `FRONTEND_PORT`), so the frontend works out of the box. Open the
  frontend from any other address (another port, `127.0.0.1`, a deployed host) and that address has to be added, for example
  `CORS_ALLOWED_ORIGINS=http://localhost:3000,https://school.example` in `.env`.
- **No wildcard.** `*`, `null`, a path, a query or credentials are refused, and the API stops at startup with a message
  naming the setting, so a typo cannot silently turn into "the browser blocks everything".
- **Empty means off.** `CORS_ALLOWED_ORIGINS=` allows no browser origin (right when the frontend and the API share one origin).
  Deleting the line, as opposed to leaving it empty, restores the default.
- Only `GET`, `POST` and `DELETE` are allowed, with the `Content-Type` and `X-Student-Id` request headers; pages can read the
  `X-Correlation-Id` response header. Errors carry the CORS headers too, so a page can show the `message` of a 4xx.
- `curl`, tests and other servers send no `Origin` header and are not affected.

CORS is something browsers enforce for their pages; it is not access control. A request from another origin is still
answered, the browser just will not hand the answer to that page.

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
cd backend                           # the .NET solution lives here; the commands below run from this folder
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

## Run the frontend tests

The web app's tests (Vitest, Testing Library and MSW, which fakes the API at the network boundary) need no backend and no
database. Two ways:

```bash
docker build --target test ./frontend    # nothing but Docker: installs the locked dependencies on Node 22 and runs the tests
cd frontend && npm ci && npm test        # on the host: Node 20.19+ or 22.12+
```

The Docker build fails if a test fails. `docker compose up` builds only the web app's last stage, so starting the stack never
runs the tests.

## Run without Docker (optional)

Needs the .NET 8 SDK and a PostgreSQL reachable at `localhost:5432` with database `school_admission` and user/password
`postgres`/`postgres` (the Development settings in `backend/src/Api/appsettings.Development.json`). For example:

```bash
docker run -d --name school-pg -p 5432:5432 -e POSTGRES_DB=school_admission -e POSTGRES_PASSWORD=postgres postgres:16-alpine
cd backend
dotnet run --project src/Api
```

The API then listens on <http://localhost:8080> (the `http` launch profile) and applies the migrations on startup. Override the
connection with the `ConnectionStrings__Postgres` environment variable.

## Project layout

```
backend/
  src/Api/             controllers, DTOs, services, middleware, background expiry job, Program.cs
  src/Domain/          entities and the hold-status rule
  src/Infrastructure/  DbContext, EF configuration, migrations, options
  tests/UnitTests/         no database
  tests/IntegrationTests/  real PostgreSQL via Testcontainers
  Dockerfile           the backend image
  SchoolAdmission.sln
frontend/
  src/                 React app (pages, api client, identity) and its tests (*.test.ts[x], src/test/)
  Dockerfile           deps, test and build stages, then nginx serving the build
  nginx.conf           single-page-app fallback, /config.js never cached
  docker-entrypoint.d/ writes /config.js from API_BASE_URL when the container starts
docs/
  API_CONTRACT.md      the frozen request/response contract
  ARCHITECTURE.md      design decisions: backend and frontend, under separate headings
  PROGRESS.md          build log: backend sessions and frontend sessions
docker-compose.yml     PostgreSQL + backend + frontend
.env.example           every setting, with its default
ASSUMPTIONS.md         decisions the brief did not specify
```
