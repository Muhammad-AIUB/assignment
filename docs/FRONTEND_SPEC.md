# Frontend spec

## Identity
No login. A single input at the top of every page for the student's
17-digit registration number. Once entered, persist it in localStorage
(key: `studentId`) and send it as the `X-Student-Id` header on every
relevant request. On app load, read it from localStorage if present so a
returning student doesn't retype it.

## Screens
1. **Class list** (`/`) — table/list of classes: name, total/held/confirmed/
   available seats, an "Apply" button per class (disabled if
   availableSeats === 0). Poll GET /classes every 3-5 seconds, no manual
   refresh needed.
2. **Create class** (`/admin/new-class`) — admin-only in spirit (no real
   auth required). Form: name, totalSeats. On submit, POST /classes, show
   success and redirect to class list.
3. **Application status** (`/holds/:holdId`) — shows current status, a live
   countdown to expiresAt, "Pay admission fee" button (disabled once expired or
   confirmed), "Cancel" button (disabled once not HELD). Poll
   GET /holds/:holdId every few seconds AND run a local countdown timer so
   the UI goes to EXPIRED instantly at zero without waiting for the next
   poll.
4. **My applications** (`/me`) — list from GET /me/holds, each row links to
   its application status screen.

## Error handling
Every API error (4xx) must render a plain-language message, never a blank
screen or console-only error. Map known error codes (SEAT_UNAVAILABLE,
DUPLICATE_APPLICATION, HOLD_EXPIRED, VALIDATION_ERROR, etc. — see
docs/API_CONTRACT.md) to short messages as returned by the
backend's `message` field; fall back to a generic "Something went wrong, please try again" for anything unmapped. Wrap the app in a React error
boundary so a render crash never produces a fully blank page.

## Resilience
On page refresh or reopening the browser, the student must still see their
ongoing application(s) — driven entirely by the persisted studentId in
localStorage plus GET /me/holds, not by any in-memory state.

## Env config
Backend base URL must be resolvable at container runtime, not baked at
Vite build time (see ARCHITECTURE.md decision on runtime config).
