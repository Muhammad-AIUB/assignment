# AI Usage

**Tool:** Claude Code (Anthropic), used for effectively the whole implementation — backend, frontend,
tests, Docker, and documentation — one narrowly-scoped session at a time, each reading the project's own
docs (`CLAUDE.md`, `docs/API_CONTRACT.md`, `docs/BUSINESS_RULES.md`, `docs/FRONTEND_SPEC.md`,
`docs/PROGRESS.md`) before starting, and updating `docs/PROGRESS.md` and committing at the end of its own
scope. Every session is listed there in full, including what each one verified and what it left open.

## Where it went wrong, and how that was caught

- **A placeholder shipped as if it were the screen.** The "My applications" page stayed the Session 11
  three-line placeholder for several sessions while other work continued around it; nothing in that
  session's own report said so plainly. Caught by Session 22, which ran the test plan against the real
  running app in a real browser rather than trusting each session's self-report, and found `/me` made no
  request at all. Fixed in Session 23, then independently re-checked against the actual code and a fresh
  browser run before being accepted.
- **An error boundary that looked complete but caught nothing.** The boundary existed, but a forced render
  error still blanked the whole page. Same root cause as above: only found because Session 22 tested the
  real behavior instead of taking "done" at face value.
- **A silent capacity bug in the concurrency bonus.** Two backend processes against one database never
  oversold a seat, but one instance could quietly starve the other's database connections under load
  (`400 UNEXPECTED_ERROR`, not a seat-safety failure). Caught only by actually running two processes under a
  real burst (Session 22); fixed with an explicit per-instance connection cap (Session 24), then
  re-verified with the exact same burst.
- **A framework edge case produced the generic error code.** A `charset=utf-16` request body crashed the
  framework's decoder and fell into the catch-all `UNEXPECTED_ERROR` instead of a specific 4xx. Caught by
  deliberately sending malformed requests at every endpoint (Session 22's "rule 7 sweep"), not by normal
  happy-path testing, and fixed in Session 24.
- **A raw backend string shown to the student.** Applying with no registration number sent the request
  anyway and showed the backend's own sentence, which names an HTTP header a student has no reason to know.
  Only surfaced by browser testing of that specific edge case (Session 22, finding 5); fixed in Session 25.
- **A dependency pin that worked locally and failed on a clean machine.** After moving the backend into
  `backend/`, floating package versions (`8.*`) resolved to an EF Core patch whose companion package isn't
  published; a warm NuGet cache had been hiding it. Caught by a clean restore, not the working copy; fixed
  by pinning exact versions.
- **A form that could be submitted three times by one click.** A fast synchronous triple submit on the
  create-class form slipped past the "disable while pending" guard because of a one-render gap, and created
  three classes instead of one. Found during that session's own browser check before it was committed, not
  after.
- **Unexplained, non-reproducing test failures, twice.** Two different full-suite runs (7 failures, then
  later 27, in different test classes each time) did not reproduce on an immediate re-run. Rather than
  silently re-running until green and saying nothing, both are written up as open questions in
  `ASSUMPTIONS.md` with the evidence and the best available explanation, because a failure that cannot be
  explained is not the same as one that has been fixed.

## What was not just taken on trust

Each fix above was re-verified against the running application — a direct API call or a real browser
session — rather than accepted on the strength of the session's own report. For the `/me` screen and the
error boundary, a separate session re-derived the root cause from the code and the browser independently of
the session that made the fix, before it was accepted as resolved.
