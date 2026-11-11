# Test Plan — School Admission System

Validates every mandatory rule, endpoint, and screen behavior in
docs/API_CONTRACT.md, docs/BUSINESS_RULES.md, and docs/FRONTEND_SPEC.md,
plus the concurrency bonus. Three layers, each proving something the layer
below it cannot:

| Level | What it proves | Tooling |
|---|---|---|
| 1 — Automated suite | Each rule holds in isolation, repeatably | `dotnet test` (xUnit + Testcontainers) / `npm test` (Vitest + RTL + MSW) |
| 2 — API verification | The real running service — not a test harness — honors the contract | HTTP requests against `docker compose up` |
| 3 — Browser E2E | Backend + frontend actually integrate end-to-end, as a real student would use it | A real browser, real clicks and typing |

Results of each run against this plan are logged in `docs/PROGRESS.md`, not duplicated here.

## 0. Environment Setup

1. `docker compose up --build` from repo root.
2. `GET /health` → 200.
3. Frontend reachable at its configured port.

## 1. Backend — Endpoint & Business Rule Verification (Level 2)

| ID | Verifies | Steps | Expected |
|---|---|---|---|
| TC-B01 | Create class, happy path | `POST /classes {name, totalSeats}` | 201, body matches contract |
| TC-B02 | Create class, invalid input | `totalSeats` ≤ 0 or missing `name` | 400, `{error,message}`, never 500 |
| TC-B03 | List classes | `GET /classes` | 200, each item shows held/confirmed/available |
| TC-B04 | Get single class | `GET /classes/{id}`, then a bogus id | 200; 404 for bogus id |
| TC-B05 | Create hold, happy path | `POST /classes/{id}/holds` with valid `X-Student-Id` | 201, status HELD, `expiresAt` present |
| TC-B06 | Seats full | Hold a class to 0, then hold again | 409, no seat corruption |
| TC-B07 | Duplicate active hold | Same student holds the same class twice | 409 (one active hold/confirmation per student per class) |
| TC-B08 | Invalid student id | Header missing or not 17 digits | 400 |
| TC-B09 | Get hold | `GET /holds/{id}` with no `X-Student-Id`; then bogus id | 200; 404 for bogus id |
| TC-B10 | Confirm, happy path | Owning student confirms a HELD hold | 200, status CONFIRMED |
| TC-B11 | Confirm idempotency | Confirm the same hold twice | Second call doesn't double-increment confirmedSeats |
| TC-B12 | Confirm by wrong student | Non-owning student tries to confirm | Rejected, seat state unchanged |
| TC-B13 | Confirm after expiry | Wait past `HOLD_DURATION_SECONDS`, then confirm | Rejected as expired, never confirmed |
| TC-B14 | Cancel a HELD hold | `DELETE /holds/{id}` while HELD | 204, held count down, available up |
| TC-B15 | Cancel a non-HELD hold | Delete an already CONFIRMED or EXPIRED hold | 409 |
| TC-B16 | My holds | `GET /me/holds` | 200, only this student's holds |
| TC-B17 | Lazy expiry correctness | Let a hold expire, immediately `GET /classes/{id}` | Available count already reflects the expiry |
| TC-B18 | Background cleanup | Wait past the worker's interval | HELD row's status physically flips to EXPIRED |
| TC-B19 | Restart persistence | Create a hold, restart the backend container | State unchanged |
| TC-B20 | Error-shape audit | Trigger ≥1 error per endpoint, plus malformed requests | Every error is `{error,message}` — never a raw 500 |

## 2. Concurrency — Bonus Verification

| ID | Verifies | Steps | Expected |
|---|---|---|---|
| TC-C01 | Single-process race | Fire N parallel holds at a class with < N seats | Exactly `totalSeats` succeed; rest 409 |
| TC-C02 | Dual-process race (the bonus) | Two backend instances, same DB, parallel holds at a seat-limited class | Combined successes never exceed `totalSeats`, and no instance starves the other |

## 3. Frontend — Screen & Flow Verification (Level 3)

| ID | Verifies | Steps | Expected |
|---|---|---|---|
| TC-F01 | Identity gate | Open `/` with empty localStorage | Prompted for reg. number; non-17-digit input rejected inline |
| TC-F02 | List + polling | Wait with no manual refresh while backend state changes | Seat counts update on their own |
| TC-F03 | Disabled at zero | A class at 0 available seats | Apply button disabled |
| TC-F04 | Apply happy path | Click apply on a class with seats | Navigates to `/holds/:holdId`, HELD + live countdown |
| TC-F05 | Apply blocked (409) | Race a class to full, then click apply | Inline error, no navigation |
| TC-F06 | Create-class form | Submit invalid, then valid | Inline 400 message; then redirect + new class visible |
| TC-F07 | Countdown auto-expire | Let the countdown hit zero | UI flips to EXPIRED, buttons disabled, no refresh needed |
| TC-F08 | Confirm flow | Click confirm before expiry | Status → CONFIRMED |
| TC-F09 | Cancel flow | Click cancel on a HELD hold | Redirect to `/`; list reflects the released seat |
| TC-F10 | /me screen | Open `/me` with an active student id | All applications shown, each links out correctly |
| TC-F11 | Refresh resilience | Hard-refresh mid-flow on `/holds/:holdId` | Same state restored purely from localStorage + API |
| TC-F12 | Error boundary | Simulate a runtime error / an unknown URL | Friendly fallback + retry — never a blank page |
| TC-F13 | /me without identity | Clear localStorage, open `/me` directly | Prompted for student id, doesn't crash or leak data |

## 4. Exit Criteria

- Every TC-B** passes against the real running backend.
- TC-C01 and TC-C02 both hold under load.
- Every TC-F** passes through actual browser interaction.
- No test case anywhere produces a raw 500 or an unhandled exception page.
- Results logged in `docs/PROGRESS.md` before submission.
