# If this went to production

Not required by the brief; written because it was asked for. What this implementation already has that
happens to be production-leaning, and what a real deployment would still need on top of it.

## Already here, not just for the assignment

- No overselling under concurrency, proven with two independent processes against one database, not just
  one process — the actual shape a real horizontally-scaled deployment would take.
- All state in PostgreSQL, nothing load-bearing in process memory: a pod restart or redeploy loses nothing.
- Every error, expected or not, answered as `{error, message}` with a 4xx and a correlation id — never a
  stack trace or a 500 for a predictable failure.
- Structured logging (Serilog) with a correlation id on every log line of a request.
- A real `/health` endpoint suitable for a container orchestrator's liveness/readiness probe.
- Swagger/OpenAPI on by default, so the contract is machine-checkable, not just documented in prose.
- Migrations applied automatically at startup from a single source of truth (EF Core), not a manual step.

## What would still need to change

**Identity and access**
- Replace the bare `X-Student-Id` header with real authentication (a login, a token with an expiry, a way
  to revoke it). Today anyone who knows or guesses a 17-digit number can act as that student.
- Add an authenticated admin role for class creation; today it's an unauthenticated endpoint, acceptable
  only because the brief has no login at all.

**Payment**
- Replace "confirm = payment succeeded" with a real payment gateway, a webhook/callback flow, and a state
  that distinguishes "payment initiated" from "payment confirmed" (today it's a single atomic step).

**Operations**
- Metrics (request rates, error rates, pool utilization, hold-expiry lag) exported to something a
  dashboard and an alert can read — today the only signal is logs.
- Distributed tracing across the seat-claim path once there's more than one service.
- A migration strategy that doesn't apply schema changes at every pod's startup (a dedicated migration job
  or step, so N replicas starting together don't race each other's migrations).
- Database backups and a tested restore procedure; the current setup has a volume, not a backup.
- Autoscaling policy for the backend, now that it's known to be safe to run more than one instance — and a
  load balancer / ingress in front of them instead of a single published port.

**Security hardening**
- Rate limiting per student id and per IP (nothing stops one script from holding every seat).
- TLS termination (today it's plain HTTP, noted as a deliberate assignment-scope decision in
  ASSUMPTIONS.md).
- Secrets (DB password, any future payment keys) from a secret manager, not `.env`.
- A request body size limit lower than the default, and the same malformed-input sweep this project
  already ran, re-run against whatever changes.

**Frontend**
- Validate response shapes at the network boundary (today a wrong-shaped response is contained by the
  error boundary, not prevented).
- An i18n layer instead of hand-written Bangla/English strings, if the school ever needs a third language.
- A accessibility pass with an actual screen reader (today's checks are Chromium-only, informed by ARIA
  attributes but not reader-tested).

**Process**
- CI running the full test suite (backend and frontend) and the Docker build on every push, rather than
  relying on a developer to run `dotnet test` / `npm test` locally before committing.
- The two unresolved "unexplained, non-reproducing test failures" entries in `ASSUMPTIONS.md` would be
  worth chasing down properly with dedicated, resource-constrained CI runners, rather than attributed to
  "probably machine load" on a shared development laptop.
