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
