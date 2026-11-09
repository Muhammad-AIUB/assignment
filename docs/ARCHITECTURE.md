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
