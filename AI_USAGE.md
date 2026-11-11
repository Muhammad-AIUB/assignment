# AI Usage

**Tool:** Claude Code (Anthropic), directed through the whole implementation — backend, frontend, tests,
Docker, and documentation — as one narrowly-scoped session at a time. Each session was scoped by me before
it started, pointed at the project's own docs (`CLAUDE.md`, `docs/API_CONTRACT.md`,
`docs/BUSINESS_RULES.md`, `docs/FRONTEND_SPEC.md`, `docs/PROGRESS.md`), and its output was read and checked
against the running application before I accepted it, asked for changes, or moved on. `docs/PROGRESS.md`
is the record of that: every session is listed there in full, including what was verified and what was
left open, because a report I hadn't checked is not the same as a report I had.

**How it was actually used:** as a fast first-draft generator and a pair of hands for mechanical work
(writing boilerplate, running test suites, drafting docs), never as the source of the decisions that
mattered — the data model, the concurrency approach, which bugs were acceptable to ship with documented,
and what "done" meant for a given session. Those calls were mine. The clearest example of that stance is
Session 22: instead of taking any earlier session's own "done" at face value, I had it run the full test
plan against the real, running app in a real browser, purely to check whether the self-reports held up.
They mostly didn't — see below. That session existed because I assumed AI-reported completion needed
independent checking, not because something had already gone visibly wrong.

## Where it went wrong, and how I caught it

- **A placeholder shipped as if it were the screen.** The "My applications" page stayed the Session 11
  three-line placeholder for several sessions while other work continued around it; nothing in that
  session's own report said so. I caught this in Session 22 by running the test plan against the real app
  myself rather than trusting the self-report, and found `/me` made no request at all. I had it fixed in
  Session 23, then checked the fix myself against the actual code and a fresh browser run before accepting
  it.
- **An error boundary that looked complete but caught nothing.** The boundary existed, but a forced render
  error still blanked the whole page. Same pattern as above: I only found it because I tested the real
  behavior in Session 22 instead of taking "done" at face value.
- **A silent capacity bug in the concurrency bonus.** Two backend processes against one database never
  oversold a seat, but I found that one instance could quietly starve the other's database connections
  under load (`400 UNEXPECTED_ERROR`, not a seat-safety failure) — only visible because I had it run two
  processes under a real burst (Session 22) rather than trusting the single-instance tests. I had it fixed
  with an explicit per-instance connection cap (Session 24), then re-ran the exact same burst myself to
  confirm the fix held.
- **A framework edge case produced the generic error code.** A `charset=utf-16` request body crashed the
  framework's decoder and fell into the catch-all `UNEXPECTED_ERROR` instead of a specific 4xx. I found this
  by deliberately sending malformed requests at every endpoint myself (Session 22's "rule 7 sweep"), not by
  normal happy-path testing, and had it fixed in Session 24.
- **A raw backend string shown to the student.** Applying with no registration number sent the request
  anyway and showed the backend's own sentence, which names an HTTP header a student has no reason to know.
  I only found this by deliberately testing that edge case in a browser myself (Session 22, finding 5); had
  it fixed in Session 25.
- **A dependency pin that worked locally and failed on a clean machine.** After moving the backend into
  `backend/`, floating package versions (`8.*`) resolved to an EF Core patch whose companion package isn't
  published; a warm NuGet cache had been hiding it. I caught this with a clean restore, not the working
  copy, and had it fixed by pinning exact versions.
- **A form that could be submitted three times by one click.** A fast synchronous triple submit on the
  create-class form slipped past the "disable while pending" guard because of a one-render gap, and created
  three classes instead of one. I found this in that session's own browser check before accepting the work,
  not after.
- **Unexplained, non-reproducing test failures, twice.** Two different full-suite runs (7 failures, then
  later 27, in different test classes each time) did not reproduce on an immediate re-run. Rather than
  having it silently re-run until green and calling that a fix, I had both written up as open questions in
  `ASSUMPTIONS.md` with the evidence and the best available explanation — a failure I can't explain is not
  the same as one that's been fixed, and I was not willing to represent it as the latter.

## What was not just taken on trust

I re-verified every fix above against the running application myself — a direct API call or a real browser
session — rather than accepting it on the strength of a session's own report. For the `/me` screen and the
error boundary specifically, I had a separate, later session re-derive the root cause from the code and the
browser independently of the session that made the fix, before I accepted it as resolved. The pattern
throughout was the same one a senior engineer would use with any assistant, human or AI: delegate the
mechanical work, scope it narrowly, and check the result against reality yourself before it counts as done.
