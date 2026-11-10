// Where the student's registration number lives.
//
// This module is the single owner of the value: it validates it, keeps it in
// memory, persists it to localStorage, and lets code outside React (the axios
// request interceptor in ../api/client.ts) read it. <StudentIdProvider> exposes
// the same value to components as a React context; because both read this one
// store they cannot disagree, and a request made in the very first commit of the
// app already sees the stored id (an effect-based hand-off from React state to
// the interceptor would not guarantee that).
//
// See docs/ARCHITECTURE.md ("Session 12").

/** localStorage key (fixed by docs/FRONTEND_SPEC.md). */
export const STUDENT_ID_STORAGE_KEY = "studentId";

// Exactly 17 ASCII digits. In JavaScript `$` (without the `m` flag) only matches
// at the very end of the string, so unlike .NET a trailing "\n" is rejected.
const STUDENT_ID_PATTERN = /^[0-9]{17}$/;

export function isValidStudentId(value: string): boolean {
  return STUDENT_ID_PATTERN.test(value);
}

// localStorage can throw (blocked site data, private windows, quota), so every
// access is guarded: the app keeps working with the id held in memory only.
function readStored(): string | null {
  try {
    const value = window.localStorage.getItem(STUDENT_ID_STORAGE_KEY);
    // A value that is not a valid id (edited by hand, written by an older
    // version) is ignored rather than sent to the API.
    return value !== null && isValidStudentId(value) ? value : null;
  } catch {
    return null;
  }
}

function writeStored(value: string): void {
  try {
    window.localStorage.setItem(STUDENT_ID_STORAGE_KEY, value);
  } catch {
    // Not persisted; the in-memory value still applies for this page load.
  }
}

// `undefined` means "not read from localStorage yet". Reading lazily on first
// use keeps importing this module free of side effects.
let current: string | null | undefined;
const listeners = new Set<() => void>();

/** The current student id, or null when none has been entered. */
export function getStudentId(): string | null {
  if (current === undefined) {
    current = readStored();
  }
  return current;
}

/**
 * Accepts `value` only if it is exactly 17 digits: stores it, persists it to
 * localStorage and notifies subscribers. Returns false, changing nothing, for
 * anything else.
 */
export function setStudentId(value: string): boolean {
  if (!isValidStudentId(value)) {
    return false;
  }
  if (value !== getStudentId()) {
    current = value;
    writeStored(value);
    listeners.forEach((listener) => listener());
  }
  return true;
}

/** Subscribe to changes; returns the unsubscribe function. */
export function subscribeStudentId(listener: () => void): () => void {
  listeners.add(listener);
  return () => {
    listeners.delete(listener);
  };
}

/**
 * Test-only. The id is cached in module state (`current`), so clearing
 * localStorage between tests does not forget it. Calling this makes the next
 * getStudentId() read localStorage again, and drops every subscriber.
 */
export function resetStudentIdStoreForTests(): void {
  current = undefined;
  listeners.clear();
}
