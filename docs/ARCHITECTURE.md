# Architecture Decisions

Append-only decision log. One entry per decision: date, decision, reason.

## Session 1 — Database layer

- **String IDs with prefixes** (`c_<guid>`, `h_<guid>`) generated in the entity, matching the contract's id format. Columns are `varchar(64)`.
- **Status stored as string** (`Held`, `Confirmed`, `Expired`, `Cancelled`) via EF value conversion; readable in SQL and safe against enum reordering. The API layer maps to the contract's uppercase values later.
- **Timestamps are UTC** `timestamptz` (`DateTime`, Kind=Utc required by Npgsql).
- **Indexes on `holds`:** `(ClassId, StudentId, Status)` for the "active hold exists" check; `(ClassId, Status)` for seat counting; `(Status, ExpiresAt)` for expiry sweeps; `(StudentId)` for `GET /me/holds`.
- **Constraints:** `classes.TotalSeats > 0` check; FK `holds.ClassId -> classes.Id` with `RESTRICT`.
- **Not added yet (open for the hold-creation session):** a partial unique index on `(ClassId, StudentId)` where `Status IN ('Held','Confirmed')` would enforce the one-active-hold rule at the DB level. Deferred because expired-but-unswept HELD rows would block re-holding until business rules define expiry handling.
- **`HOLD_DURATION_SECONDS`** read from configuration/env var (default 600, must be > 0, validated on startup) into `HoldOptions`, injectable as `IOptions<HoldOptions>`.
- **Migrations** live in `Infrastructure/Persistence/Migrations` and are applied automatically on Api startup. A design-time factory lets `dotnet ef` run without booting the Api host. `dotnet-ef` is a local tool (`dotnet tool restore`).
- **Missing `ConnectionStrings:Postgres`** now fails fast at startup with a clear message.

## Session 2 — Class endpoints (POST /classes, GET /classes, GET /classes/{id})

- **Service layer lives in `Api/Services`** (`IClassService`/`ClassService`) and uses `AppDbContext` directly; controllers only validate -> call service -> map to HTTP. A repository layer was not added: nothing needs it yet.
- **Seat counts in one SQL statement**: held/confirmed are correlated `COUNT` subqueries per class (backed by `ix_holds_class_status`). Expired-but-unswept HELD rows (`ExpiresAt <= now`) are excluded from `heldSeats`. `availableSeats = max(0, total - held - confirmed)`, computed in memory after the query (EF could not translate `Math.Max` inside a DTO constructor projection).
- **Clock via `TimeProvider`** (singleton `TimeProvider.System`) so "now" is injectable.
- **Standard error shape for validation**: `InvalidModelStateResponseFactory` returns `{ "error": "VALIDATION_ERROR", "message": ... }` with 400 for both FluentValidation failures and model-binding errors (missing/malformed body). Unknown class -> 404 `CLASS_NOT_FOUND`.
- **Validation**: `name` non-empty (whitespace rejected), max 200 chars (matches the column, so over-long names are a 400 rather than a DB error); `totalSeats > 0`. Name is trimmed before saving.
- **Not yet built**: global exception-handling middleware (unexpected exceptions still fall to the framework default).

## Session 3 — POST /classes/{classId}/holds (concurrency control)

**Note:** `docs/BUSINESS_RULES.md` still does not exist in the repo. "Rule 1" was implemented as *seats are never oversold (held + confirmed <= total)* and "rule 3" as *a student has at most one active (HELD/CONFIRMED) hold per class*, as described in the session prompt and API contract.

**Approach: atomic conditional UPDATE on the class row, in one transaction (READ COMMITTED).**
`classes` gained denormalised counters `HeldSeats` and `ConfirmedSeats`. Creating a hold runs:

1. *(cleanup)* Flip this class's expired HELD holds to EXPIRED and decrement `HeldSeats` by exactly the number flipped (single CTE statement).
2. *(seat claim)* `UPDATE classes SET HeldSeats = HeldSeats + 1 WHERE Id = @id AND HeldSeats + ConfirmedSeats < TotalSeats RETURNING HeldSeats`. No row returned = sold out -> 409 `NO_SEAT_AVAILABLE`.
3. *(rule 3)* Check for an existing HELD/CONFIRMED hold for (class, student). If found, **roll back** (undoing step 2) -> 409 `DUPLICATE_ACTIVE_HOLD`.
4. Insert the `Hold` row and commit.

**Why this is safe under concurrency and across processes**
- The check ("is there a seat?") and the reservation (increment) are one SQL statement, so there is no read-then-write gap for another request to slip into. The decision is made by PostgreSQL against the current committed row, not by app memory that may be stale.
- The UPDATE takes a row-level lock on the class row that is held until the transaction ends. A concurrent UPDATE on the same row blocks, then (READ COMMITTED) re-evaluates the `WHERE` against the freshly committed row. With N seats left, exactly N claims match; later ones match zero rows.
- All of this lives in the database, so it holds regardless of how many API processes/threads run against it. No in-process locks or caches are involved.
- **Rule 3 is race-safe** because step 3 runs *after* the class-row lock is acquired in step 2: all create-hold transactions for a class are serialised from that point to commit, so a competing transaction's hold is either already committed (and visible to the check's fresh statement snapshot) or has not yet reached its own check. Two simultaneous requests from the same student therefore cannot both pass. Different classes do not contend with each other.
- **Defence in depth:** `ck_classes_seats_not_oversold` (CHECK held >= 0, confirmed >= 0, held + confirmed <= total) makes the DB refuse an oversell even if code were wrong, and the partial unique index `ux_holds_active_student_class` on `(ClassId, StudentId) WHERE Status IN ('Held','Confirmed')` enforces rule 3 at the DB level (a violation is mapped to the same 409). This resolves the Session 1 deferral: expired-but-unswept rows are now flipped to EXPIRED in step 1 before the insert, so they no longer block a re-hold.
- No deadlocks: the transaction only locks hold rows in step 1 (before taking the class lock in step 2 for the same class), and never locks hold rows after the class lock, so there is no lock-order cycle.

**Other decisions**
- Raw ADO.NET commands on EF's transaction are used for the two statements because EF's `SqlQuery` wraps SQL in a subquery, which Postgres cannot do for `UPDATE ... RETURNING`.
- `GET /classes` still derives counts from `holds` (Session 2); the new counters are authoritative for seat admission. They differ only transiently for expired-but-unswept holds, which `GET` already excludes. Later sessions (confirm / cancel / expiry sweep) must update the counters with the same atomic-UPDATE style.
- Invalid `X-Student-Id` -> 400 `INVALID_STUDENT_ID`; unknown class -> 404 `CLASS_NOT_FOUND`; the header is validated before the class lookup.
- Migration `AddSeatCountersAndActiveHoldIndex` backfills counters from existing rows.

## Session 4 — GET /holds/{id}, POST /holds/{id}/confirm, DELETE /holds/{id}, GET /me/holds

**Note:** `docs/BUSINESS_RULES.md` still does not exist. As in Session 3, the rules were taken from the session prompt and `API_CONTRACT.md`: *rule 2* = expiry is computed lazily (a HELD hold past `ExpiresAt` is EXPIRED even if nothing has swept it), *rule 5* = confirm is idempotent for the owner and never counts a seat twice, and the delete assumption = only the owner can cancel, only a HELD (unexpired) hold, anything else is 409.

**Lazy expiry (rule 2).** `Hold.EffectiveStatus(stored, expiresAt, now)` (Domain) is the single definition: stored `Held` with `ExpiresAt <= now` is `Expired`; every other status is returned as stored. `GET /holds/{id}` and `GET /me/holds` apply it in memory to the stored row, so no background job is needed to report EXPIRED. GETs never write. The boundary (`ExpiresAt <= now` is expired) matches the SQL in the seat claim and cleanup.

**Confirm and cancel share one atomic transition** (`HoldService.TransitionAsync`), the same pattern as the Session 3 seat claim, one statement that decides and applies:

```sql
WITH moved AS (
    UPDATE holds SET <Status = 'Confirmed', ConfirmedAt = @now | Status = 'Cancelled'>
    WHERE "Id" = @holdId AND "StudentId" = @studentId AND "Status" = 'Held' AND "ExpiresAt" > @now
    RETURNING "ClassId")
UPDATE classes SET <HeldSeats - 1, ConfirmedSeats + 1 | HeldSeats - 1>
WHERE "Id" IN (SELECT "ClassId" FROM moved)
RETURNING "Id"
```

- The `WHERE` on the hold row *is* the rule check (owner, still HELD, not expired). Only a row that passes it changes status, and the class counters move only for that returned row, in the same statement and transaction. Counters therefore cannot drift from hold statuses.
- Concurrent requests on the same hold queue on the hold row lock and re-check the guard under READ COMMITTED. So N parallel confirms by the owner produce one transition and the other N-1 fall through to the 200 idempotent path with the counters untouched; a confirm racing a cancel has exactly one winner; N parallel cancels release the seat exactly once. Tests cover all three.
- Delete releases the seat in the same statement (`HeldSeats - 1`), so the seat is available immediately, not at expiry. A cancelled hold is no longer in the partial unique index, so the student may hold again.
- Lock order is hold row then class row, the same as the Session 3 expiry cleanup, so there is no deadlock cycle with hold creation.
- Raw ADO.NET is used through the existing `ExecuteScalarAsync` helper (EF cannot express a data-modifying CTE). The SQL fragments that vary are compile-time constants; request values are always parameters.

**When the guarded UPDATE matches nothing** the reason is read from committed state (the UPDATE waited on any competing transaction, so that state is final), in this precedence: no row -> 404 `HOLD_NOT_FOUND`; not the owner -> 409 `NOT_HOLD_OWNER` (checked before status, so another student cannot learn a hold's state and cannot use the idempotent path); then by effective status: Confirmed -> 200 for confirm (idempotent) / 409 `HOLD_ALREADY_CONFIRMED` for delete; Cancelled -> 409 `HOLD_CANCELLED`; Expired -> 409 `HOLD_EXPIRED`. An active, owned HELD hold with a failed guard is unreachable, so that case throws.

**Expired holds are not written by confirm/cancel.** An expired-but-unswept hold is rejected with 409 and left as is; confirm/cancel never touch it, so they cannot corrupt `HeldSeats`. It is still not counted by `GET /classes`, and the next `POST /classes/{id}/holds` for that class sweeps it (Session 3 step 1). A standalone background sweep was not added; it is not needed for correctness.

**Other decisions**
- `X-Student-Id` is validated (400 `INVALID_STUDENT_ID`) before any lookup on confirm, delete and `/me/holds`, reusing one controller helper that `POST /classes/{id}/holds` now also uses. `GET /holds/{id}` takes no header (per the contract) and is readable by anyone who knows the id.
- `GET /me/holds` returns the student's holds of every status (the contract's four statuses all apply), newest first (`CreatedAt` desc, then `Id`), joined to the class name; an empty list is `[]`. Served by `ix_holds_student`.
- Response bodies are exactly the contract's fields; `GET /holds/{id}` does not include `studentId`.

## Session 5 — Background expiry sweep, GET /health

**Note:** `docs/BUSINESS_RULES.md` still does not exist; nothing in this session needed a business rule beyond Sessions 2-4.

**The expiry sweep is housekeeping, not correctness.** A hold past `ExpiresAt` is already EXPIRED as far as the API is concerned, by computed logic that needs no sweep: `Hold.EffectiveStatus` (GET /holds, GET /me/holds), the expired-excluding counts in GET /classes, and the per-class cleanup at the top of the seat claim in POST /classes/{id}/holds. The sweep only makes the stored rows and the `HeldSeats` counters match, for data cleanliness. This is stated in code comments on `HoldExpiryWorker` and `HoldExpirySweeper`, and a test (`Sweep_changes_nothing_the_api_reports`) checks the API gives identical answers before and after a sweep.

**Shape.** `IHoldExpirySweeper`/`HoldExpirySweeper` (Api/Services) does one sweep and returns how many holds it expired. `HoldExpiryWorker` (Api/BackgroundServices, a `BackgroundService`, so an `IHostedService`) calls it from a `PeriodicTimer`, once immediately on start and then every interval, in a fresh DI scope per tick (the sweeper owns a scoped `DbContext`). Config, read like `HOLD_DURATION_SECONDS`: `HOLD_EXPIRY_SWEEP_INTERVAL_SECONDS` (default 5, must be > 0, validated on startup) and `HOLD_EXPIRY_SWEEP_ENABLED` (default true). A failed sweep is logged as a warning and retried next tick; it never stops the host. The shared test factory sets `HOLD_EXPIRY_SWEEP_ENABLED=false` so tests that deliberately leave expired rows unswept (to prove lazy expiry) are not changed under them; the sweeper tests call the sweeper directly and one test turns the worker on for its own host.

**The sweep statement** (one SQL statement per batch of 500, in a READ COMMITTED transaction; the `(Status, ExpiresAt)` index from Session 1 serves it):

```sql
WITH due AS (SELECT "Id" FROM holds WHERE "Status" = 'Held' AND "ExpiresAt" <= @now
             ORDER BY "ExpiresAt" LIMIT @batch FOR UPDATE SKIP LOCKED),
     expired AS (UPDATE holds h SET "Status" = 'Expired' FROM due
                 WHERE h."Id" = due."Id" AND h."Status" = 'Held' AND h."ExpiresAt" <= @now
                 RETURNING h."ClassId"),
     per_class AS (SELECT "ClassId", count(*) AS n FROM expired GROUP BY "ClassId"),
     released AS (UPDATE classes c SET "HeldSeats" = c."HeldSeats" - per_class.n
                  FROM per_class WHERE c."Id" = per_class."ClassId" RETURNING 1)
SELECT count(*) FROM expired
```

- **Counters cannot drift.** The decrement per class is the number of rows the guarded UPDATE actually flipped (`RETURNING`), applied in the same statement and transaction, the same pattern as Sessions 3-4. This relies on the invariant `HeldSeats = number of HELD rows` (unswept expired rows included), which every writer and the Session 3 migration backfill maintain. Data that breaks it makes the DB's `ck_classes_seats_not_oversold` reject the sweep loudly rather than hiding the drift.
- **Never waits on a hold row** (`SKIP LOCKED`): a row another request holds is being dealt with by that request. The class row is updated only after `per_class` has consumed every flipped row, so the sweep never holds the class lock while still locking hold rows.
- **One sweep at a time** across all API instances, via `pg_try_advisory_xact_lock` taken inside the transaction (released at commit/rollback, so a crash cannot leave it behind). A sweep that does not get the lock skips; the holder is doing the same work. This also removes any class-row lock-order question between two sweeps.
- A backlog larger than one batch is drained by looping until a batch comes back short.
- Confirmed and cancelled holds are never touched, whatever their `ExpiresAt`.

**Why hold creation does not use `SKIP LOCKED` (and the sweep does).** If a request skipped rows that a sweep had locked but not yet released, it could claim a seat while the sweep's decrement was still uncommitted and see a false NO_SEAT_AVAILABLE for a seat that has in fact expired. So hold creation waits for those rows (correct counters once it proceeds), which is why its cleanup must lock in a fixed order (next point). The sweep has no such concern: skipping a row only means it is cleaned up a moment later.

**Correction to Session 3: the hold-creation cleanup could deadlock.** Session 3 stated "no deadlocks". The race test written for this session disproved it: when many `POST /classes/{id}/holds` requests reach a class with a pile of expired-but-unswept holds, each runs the step-1 cleanup over the same rows, and each keeps every row it touches locked until commit, in whatever order its scan meets them, so two requests can end up holding each other's next row. Postgres reported `40P01 deadlock detected` ("while locking tuple ... in relation holds") and the loser got HTTP 500, which the project forbids for a predictable situation. It reproduced with **no sweep running at all** (in two 40-round stress runs with sweeps off, 29 and 27 requests failed). Fix, in `HoldService.CreateAsync` step 1: the rows are locked in one fixed order (`SELECT "Id" ... ORDER BY "Id" FOR UPDATE` in a `MATERIALIZED` CTE, so no request can hold a row another wants while wanting one it holds), and the class row is updated only after a `count(*)` over all flipped rows, so it is never locked while hold rows are still being locked. Only the lock order changed: the same rows flip and the counter drops by the same amount. After the fix the same race (with and without concurrent sweeps) ran clean repeatedly (0 deadlocks in 5 repeated runs plus the full suite), against failures on every pre-fix run. Tests: `Concurrent_hold_requests_cleaning_up_the_same_expired_holds_never_fail` and `Sweep_racing_hold_creation_keeps_counters_equal_to_the_rows`. Lock order overall is now: hold rows in Id order, then the class row; confirm/cancel (one hold row, then the class row) and the sweep (skips locked rows, then class rows) fit that order.

**Test data must respect the counter invariant.** The sweep is global, so it also reaches rows seeded by other tests in the shared database. One Session 2 test seeded HELD rows (one expired) without moving `HeldSeats`, which the sweep then correctly refused to push below zero; that test now seeds the counters to match its rows. Its assertions (GET excludes the expired hold) are unchanged.

**GET /health.** `HealthController` -> `IHealthService`/`HealthService`, controller only maps the result. The check is `SELECT 1` through the app's own `DbContext`, bounded to 3 seconds so a hung database cannot hang the probe. 200 `{ "status": "ok", "database": "up" }`; 503 `{ "error": "DATABASE_UNAVAILABLE", "message": ... }` in the standard error shape (the exception goes to the log, not the client). A client disconnect is not reported as a database failure. No `X-Student-Id`. It is an operational endpoint outside the frozen student-facing `API_CONTRACT.md`, which is left untouched. The 503 test starts a second real Postgres container and stops it mid-test, so no mocks and the shared test database is unaffected.

**Still not built:** global exception-handling middleware (unexpected exceptions still fall to the framework default).

## Session 6 — Global exception handling, correlation id, 4xx audit

**Note:** `docs/BUSINESS_RULES.md` still does not exist, so `API_CONTRACT.md` was the only documented source for the audit. This session builds the middleware that Sessions 2 and 5 listed as "not built yet".

**Pipeline order** (`Program.cs`): `CorrelationIdMiddleware` -> `UseSerilogRequestLogging` -> `ExceptionHandlingMiddleware` -> `UseStatusCodePages` -> Swagger -> controllers. The correlation id is outermost so the request-log line carries it; the exception handler sits inside request logging so that line records the status the client really received.

**Exception handling** (`Api/Middleware`). `ExceptionHandlingMiddleware` catches anything that escapes a controller or service, logs the real exception, and answers `{ "error", "message" }` with a 4xx. Controllers keep answering predictable failures themselves; this is the backstop. The decision of status/code lives in one pure function, `ExceptionMapper.Map`, which looks through wrapper exceptions (`DbUpdateException`, `AggregateException`, EF's retry wrapper `InvalidOperationException`) to the one that matters:

| Exception | Status / code |
|---|---|
| `PostgresException` 40001, 40P01, 55P03 or class 23 (serialization, deadlock, lock timeout, unique/FK/check) | 409 `CONFLICT` (caller can retry) |
| `PostgresException` class 22 (data exception: NUL byte, out-of-range value, ...) | 400 `INVALID_INPUT` |
| `BadHttpRequestException` | its own 4xx (non-4xx coerced to 400): 408 `REQUEST_TIMEOUT`, 413 `PAYLOAD_TOO_LARGE`, else 400 `BAD_REQUEST` |
| client disconnected (`RequestAborted` + `OperationCanceledException`) | logged at Information, status 499, no body; not an error |
| anything else | **400 `UNEXPECTED_ERROR`** |

- **Why 400 for the catch-all.** `CLAUDE.md` says the global handler "still returns a 4xx-shaped error", and a failure is never a 5xx. 400 is the generic 4xx; it is one constant in `ExceptionMapper` if a different status is preferred. This includes "database unreachable" on student-facing endpoints. `GET /health` keeps its Session 5 `503 DATABASE_UNAVAILABLE`: it is an operational probe outside the student contract, and its own handler already answers in the standard shape.
- **Nothing from the exception reaches the client.** Messages are fixed text; no exception message, type name, SQL or stack frames. This holds in Development too (the framework's developer exception page never sees the exception because this middleware handles it first). Tests assert a secret string in the exception text is absent from the response and present in the log.
- **If the response has already started** the status line is gone and an error body is impossible: the failure is logged at the mapped level, then `HttpContext.Abort()` so the client sees a failed response rather than a truncated one that looks complete.
- **Correlation id.** `X-Correlation-Id` from the caller is used if it matches `^[A-Za-z0-9._-]{1,64}$`, otherwise a fresh GUID (`N` format) is generated; anything else is replaced, since the value is echoed into a header and into logs. The id is set as a response header on every response (via `OnStarting`, so it survives the handler clearing a half-built response), and pushed into Serilog's `LogContext` as `CorrelationId`, so every event written during the request carries it. The console sink now uses an output template with `{Properties:j}` so the property is visible (the default template hides properties). For `UNEXPECTED_ERROR` the id is also in the message (`... Reference: <id>`), so a client report maps straight to the log lines. The error body stays exactly `{ "error", "message" }`; no extra field.

**Framework-generated 4xx now use the standard shape too.** Unknown route, wrong method and wrong content type used to come back as an empty body or `application/problem+json`. `SuppressMapClientErrors = true` stops MVC converting bare 4xx results to ProblemDetails, and `UseStatusCodePages` fills any bodiless 4xx using `HttpErrors.ForStatus`: 404 `ROUTE_NOT_FOUND`, 405 `METHOD_NOT_ALLOWED` (the `Allow` header is kept), 415 `UNSUPPORTED_MEDIA_TYPE` (also a POST with no body or content type), 406 `NOT_ACCEPTABLE`, otherwise `REQUEST_REJECTED`. Responses that already have a body (every controller error) are untouched.

**Audit of `API_CONTRACT.md` 4xx cases.** `ContractErrorCodesTests` provokes every documented case through the real endpoint and asserts the exact status, the stable error code, JSON content type and exactly the two fields `error` and `message`. 35 test rows: 34 error responses plus the idempotent owner re-confirm that must stay 200. They cover every endpoint that has a documented or implied 4xx (all except `GET /classes`, which has none), including the header-before-class-lookup order on `POST /classes/{id}/holds` and the non-owner never getting the idempotent 200 on confirm. Note the contract itself documents the 400 only for `POST /classes/{id}/holds`; the same `INVALID_STUDENT_ID` 400 on confirm, delete and `/me/holds` and the 400 `VALIDATION_ERROR` on `POST /classes` are implied by the header rule and Session 2, and are asserted too. **All of them already returned the expected status and code before this session**; no controller or service logic changed. Two real defects were found and fixed:
1. `POST /classes` with a NUL (`\u0000`) in `name` passed validation, PostgreSQL refused to store it, and the client got a **500 with a stack trace**. The validator now rejects it with 400 `VALIDATION_ERROR`, and the mapper's class-22 rule is the backstop for any other value the database refuses.
2. The framework-generated 4xx described above did not use the error shape.

**Known and left as is.**
- Kestrel rejects some requests before any middleware runs (a `%00` in the URL path, a malformed request line, oversized headers). Those get Kestrel's own bare 400/431 with no body; no middleware can shape them.
- Model-binding 400s (`VALIDATION_ERROR`) still carry the framework's wording for JSON type errors, e.g. a mention of the DTO type name. The status, code and shape are right; the text is pre-existing from Session 2.

**Testing approach.** The handler is exercised through the full pipeline in the Development environment. The thing that throws is a test double for `IClassService`; everything between it and the client is the application's real code. Log assertions use a Serilog `ILogEventSink` registered in the test host's DI (picked up by `ReadFrom.Services`), so they check what Serilog actually receives. A separate smoke run against real Kestrel and a real Postgres that was stopped mid-run confirmed the 4xx body, the response header and the ERR log line carrying the same id.

## Session 7 — Docker

No application code changed. Run it with `docker compose up --build` (API on http://localhost:8080, Swagger at `/swagger`, probe at `/health`); `docker compose down` keeps the data, `docker compose down -v` deletes it.

- **Dockerfile lives at `src/Api/Dockerfile`, build context is the repo root.** Api references Domain and Infrastructure, so the context must include `src/`. Compose sets `context: .` and `dockerfile: src/Api/Dockerfile`. Restore runs on the three csproj files alone (test projects are not copied), so the package layer is cached until a package reference changes.
- **Alpine runtime (`aspnet:8.0-alpine`) and non-root.** Small (176 MB image), ships an unprivileged `app` user (uid 1654, used via `USER app`) and has busybox `wget`, which the `HEALTHCHECK` needs; the Debian runtime image has no curl or wget. Kestrel listens on 8080 (`ASPNETCORE_HTTP_PORTS`), since a non-root user cannot bind below 1024. Alpine runs .NET in invariant-globalization mode (no ICU); the API and Npgsql need no culture data and the whole flow was exercised in the container, but revisit this if culture-specific behaviour is ever added.
- **Production environment inside the container.** `ASPNETCORE_ENVIRONMENT=Production`, so `appsettings.Development.json` (a localhost connection string) is never loaded and the connection string can only come from `ConnectionStrings__Postgres`; a missing value still fails fast at startup (Session 1). Swagger is enabled unconditionally in `Program.cs`, so `/swagger` is also served in the container; left as is.
- **Connection string is assembled in `docker-compose.yml` from `POSTGRES_DB`/`POSTGRES_USER`/`POSTGRES_PASSWORD`**, so the database and the API cannot disagree about credentials. The host name is the compose service name `postgres`. Consequence: a `;` in the password would break the connection string (noted in `.env.example`).
- **Defaults in compose, documentation in `.env.example`.** Every variable has a `${VAR:-default}` in the compose file, so `up --build` works with no `.env`; `.env.example` lists them all with the same values for people who want to override. `.env` is git-ignored and also excluded from the build context. `POSTGRES_*` only take effect when Postgres first creates its volume; changing them afterwards needs `docker compose down -v`.
- **Start order and health.** `api` waits for Postgres' `pg_isready` healthcheck (`condition: service_healthy`), then migrates and serves (migrations still run at startup, Session 1). The API healthcheck calls `GET /health`, which is 503 when the database is down, so the container then reports `unhealthy`. Docker does not restart on that alone; `restart: unless-stopped` only reacts to the process exiting.
- **Postgres is not published to the host.** Only the API needs it, and it avoids clashing with a local Postgres on 5432. `docker compose exec postgres psql ...` for inspection; a commented line in the compose file shows how to publish it. Data lives in the named volume `school-admission_postgres_data`.
- **Plain HTTP only.** There is no TLS termination; put a reverse proxy in front for anything beyond local use.
