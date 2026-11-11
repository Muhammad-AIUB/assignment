# Business Rules

These are the rules that make this a correct admission system, not just a
CRUD API. Every one of them must hold against the **running** service, not
only in the automated test suite — see docs/TEST_PLAN.md for how each is
verified end-to-end (automated / API / browser).

## Mandatory rules

1. **Seat safety under concurrency.** `held + confirmed` can never exceed
   `totalSeats` for a class, even when many requests race for the same
   class at once, and even across two backend processes sharing one
   database. Enforced by a single atomic conditional `UPDATE ... WHERE
   held + confirmed < totalSeats RETURNING ...` inside a DB transaction —
   never by in-process locking, since that breaks under multiple instances.
   *Verified by: TC-B06, TC-C01, TC-C02.*

2. **Hold auto-expiry.** A HELD hold expires `HOLD_DURATION_SECONDS` after
   creation. Available-seat counts are always computed to exclude expired-
   but-not-yet-cleaned HELD rows, so correctness never depends on the
   background worker having run yet. The worker only performs data hygiene
   (physically flipping stale rows to EXPIRED) — it is not on the
   correctness path.
   *Verified by: TC-B17 (lazy correctness), TC-B18 (worker hygiene).*

3. **One active hold or confirmation per student per class.** A student
   cannot hold or confirm the same class twice while a previous hold is
   still HELD or CONFIRMED.
   *Verified by: TC-B07.*

4. **Only the owning student can confirm.** A HELD hold can only be
   confirmed by the student who created it, and only while it is still
   HELD (not EXPIRED, not already CONFIRMED).
   *Verified by: TC-B12, TC-B13.*

5. **Confirm is idempotent.** Calling confirm twice on the same hold must
   not increment `confirmedSeats` twice.
   *Verified by: TC-B11.*

6. **No data loss across restarts.** All state lives in PostgreSQL, not
   in-memory — a backend restart must not lose or corrupt any hold/class
   state.
   *Verified by: TC-B19.*

7. **Invalid input is always a 4xx, never a 500.** Every endpoint, under
   every malformed-input condition, returns `{ "error": "CODE", "message":
   "..." }` — a raw 500 or unhandled exception reaching the client is
   always a bug.
   *Verified by: TC-B02, TC-B08, TC-B20.*

## Decisions not specified in the brief

The assignment brief left these open; here is what was decided and why, so
every future session treats them as settled rather than re-deciding them
differently each time:

- **DELETE is only valid on a HELD hold.** Deleting a CONFIRMED or EXPIRED
  hold returns 409, not 204 — cancelling something that isn't an active
  hold isn't a meaningful operation.
- **`GET /holds/{holdId}` does not require `X-Student-Id`.** Read-only
  status lookups aren't the enforcement point for identity; write
  operations (confirm, delete) are.
- **Concurrency is enforced at the database layer, not in application
  memory.** This is what makes running two backend processes against the
  same database safe — the bonus requirement — and it's why Rule 1 is
  implemented as a single atomic UPDATE rather than a lock, semaphore, or
  in-memory counter.
- **Real-time seat updates are done via polling, not WebSockets/SSE.**
  Simpler, and sufficient for the scale and UX this assignment calls for.