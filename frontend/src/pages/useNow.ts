import { useEffect, useState } from "react";

const TICK_MS = 1000;

/**
 * A timer that fires this long after the deadline rather than at it. Browsers can
 * run a timeout a millisecond before its time as `Date.now()` reads it, which
 * would leave the screen on 00:00 / HELD for a whole extra second.
 */
const DEADLINE_SLACK_MS = 10;

/** The longest delay `setTimeout` accepts (2^31 - 1 ms, about 24.8 days). */
const MAX_TIMER_MS = 2_147_483_647;

/**
 * The current time in epoch ms, renewed every second so a countdown can be drawn
 * from it.
 *
 * `deadlineMs` is the instant something has to change on screen. A one-second
 * tick alone would show that change up to a second late, so a separate timeout is
 * set for the deadline itself. Pass null when there is nothing to count down to
 * (the hold is not HELD, or its deadline could not be read): no timer runs then.
 *
 * Background tabs throttle timers, so the time is also renewed the moment the tab
 * is visible again.
 */
export function useNow(deadlineMs: number | null): number {
  const [now, setNow] = useState(() => Date.now());
  const active = deadlineMs !== null;

  useEffect(() => {
    if (!active) return;
    const tick = () => setNow(Date.now());
    tick();
    const interval = window.setInterval(tick, TICK_MS);
    const onVisible = () => {
      if (document.visibilityState === "visible") tick();
    };
    document.addEventListener("visibilitychange", onVisible);
    return () => {
      window.clearInterval(interval);
      document.removeEventListener("visibilitychange", onVisible);
    };
  }, [active]);

  useEffect(() => {
    if (deadlineMs === null) return;
    const delay = deadlineMs - Date.now();
    if (delay <= 0 || delay > MAX_TIMER_MS) return;
    const timeout = window.setTimeout(() => setNow(Date.now()), delay + DEADLINE_SLACK_MS);
    return () => window.clearTimeout(timeout);
  }, [deadlineMs]);

  return now;
}
