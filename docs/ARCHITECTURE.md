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
