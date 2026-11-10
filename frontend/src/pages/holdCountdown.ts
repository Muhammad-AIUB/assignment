// The rules of the application status screen that do not need React: reading the
// deadline, formatting the countdown, and deciding which status to show. They are
// plain functions so they can be checked on their own, away from timers and the
// network.

import type { HoldStatus } from "../api/types";

/** Under this many milliseconds left the countdown is drawn as urgent. */
export const URGENT_MS = 60_000;

/**
 * An ISO 8601 instant as milliseconds since the epoch, or NaN when it cannot be
 * read.
 *
 * The backend writes 7 fractional digits of a second when a hold is created and 6
 * when it is read later. Only milliseconds matter here, and not every browser
 * accepts more than 3 digits, so the extra ones are dropped before parsing.
 */
export function parseInstant(iso: string): number {
  return Date.parse(iso.replace(/(\.\d{3})\d+/, "$1"));
}

/**
 * "MM:SS", or "H:MM:SS" from an hour up. Rounded up to whole seconds, so the
 * display shows 00:01 until the very last instant and reaches 00:00 exactly when
 * the deadline passes (the moment the screen flips to EXPIRED). Never negative.
 */
export function formatCountdown(remainingMs: number): string {
  const totalSeconds = Math.max(0, Math.ceil(remainingMs / 1000));
  const hours = Math.floor(totalSeconds / 3600);
  const minutes = Math.floor((totalSeconds % 3600) / 60);
  const seconds = totalSeconds % 60;
  const two = (n: number) => String(n).padStart(2, "0");
  return hours > 0 ? `${hours}:${two(minutes)}:${two(seconds)}` : `${two(minutes)}:${two(seconds)}`;
}

export interface ResolveInput {
  /** The status in the latest answer from the server. */
  serverStatus: HoldStatus;
  /** The deadline in epoch ms (NaN when the server's `expiresAt` could not be read). */
  expiresAtMs: number;
  /** The current time, from the one-second timer. */
  nowMs: number;
  /** When, on this device's clock, that latest server answer arrived. */
  fetchedAtMs: number;
}

export interface ResolvedHold {
  /** The status the screen shows. */
  status: HoldStatus;
  /** Milliseconds until the deadline, 0 once it has passed; null if the deadline is unreadable. */
  remainingMs: number | null;
  /** The countdown has reached zero. The confirm button is off from here on. */
  countdownDone: boolean;
  /** EXPIRED is this device's own conclusion: the server has not said so yet. */
  expiredLocally: boolean;
  /**
   * The countdown is at zero but an answer from the server that arrived after the
   * deadline still says HELD, so the server is trusted and the screen stays HELD.
   * This is what a clock running ahead of the server's looks like.
   */
  serverDisagrees: boolean;
}

/**
 * Which status to show.
 *
 * The server is the source of truth, with one exception that is only ever a
 * stand-in: when the server last said HELD and the countdown has reached zero,
 * the screen shows EXPIRED at once instead of waiting for the next poll.
 *
 * - A server status other than HELD (CONFIRMED, EXPIRED, CANCELLED) is shown as
 *   it is, whatever the clock says. A student who confirmed on another device a
 *   moment before the deadline must not be told the hold expired.
 * - The stand-in applies only while the latest server answer is older than the
 *   deadline, because only then does that answer say nothing about it. As soon
 *   as an answer arrives from after the deadline, it decides: EXPIRED if the
 *   server agrees, HELD if it does not.
 */
export function resolveHold({ serverStatus, expiresAtMs, nowMs, fetchedAtMs }: ResolveInput): ResolvedHold {
  const readable = !Number.isNaN(expiresAtMs);
  const remainingMs = readable ? Math.max(0, expiresAtMs - nowMs) : null;
  const countdownDone = readable && nowMs >= expiresAtMs;

  if (serverStatus !== "HELD" || !countdownDone) {
    return { status: serverStatus, remainingMs, countdownDone, expiredLocally: false, serverDisagrees: false };
  }
  if (fetchedAtMs < expiresAtMs) {
    return { status: "EXPIRED", remainingMs, countdownDone, expiredLocally: true, serverDisagrees: false };
  }
  return { status: "HELD", remainingMs, countdownDone, expiredLocally: false, serverDisagrees: true };
}
