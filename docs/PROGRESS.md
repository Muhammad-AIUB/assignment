# Progress

- [x] Session 0: scaffolding — done
- [x] Session 1: database layer — done
  - Entities `Class`, `Hold`, `HoldStatus` enum (Domain)
  - `AppDbContext` + Fluent API configurations, indexes (Infrastructure)
  - `HoldOptions` (`HOLD_DURATION_SECONDS`, default 600)
  - `InitialCreate` migration, auto-applied on startup; verified against Postgres 16
  - Note: `docs/BUSINESS_RULES.md` has not been provided yet
- [x] Session 2: class endpoints — done
  - POST /classes, GET /classes, GET /classes/{classId} (controller, service, FluentValidation)
  - Seat counts via single query, expired HELD excluded from held count
  - 404/400 use the standard `{ error, message }` shape
  - Integration tests (Testcontainers Postgres): 9 passing
- [x] Session 3: POST /classes/{classId}/holds — done
  - `HoldsController` + `HoldService`: atomic conditional UPDATE seat claim, race-safe duplicate check, one transaction
  - Migration: `HeldSeats`/`ConfirmedSeats` counters, no-oversell CHECK, partial unique index on active holds
  - 400 (bad `X-Student-Id`), 404, 409 (no seat / duplicate) with standard error shape
  - Integration tests incl. 60 concurrent students on 5 seats and 30 concurrent requests from one student: 22 passing total
  - Still missing: `docs/BUSINESS_RULES.md` (rules 1 and 3 interpreted from the prompt; see ARCHITECTURE.md)
