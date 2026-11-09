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
