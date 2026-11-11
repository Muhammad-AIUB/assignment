import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { useStudentId } from "../identity/StudentIdProvider";
import { listMyHolds } from "./endpoints";
import type { MyHold } from "./types";

/** Cache key of one student's applications. The student is part of the key (see useMyHolds). */
export function myHoldsQueryKey(studentId: string | null) {
  return ["my-holds", studentId] as const;
}

/** The same pace as the class list and the status screen (docs/FRONTEND_SPEC.md: every few seconds). */
export const MY_HOLDS_POLL_MS = 4000;

/**
 * What the my-applications screen has to draw. Exactly one of these at a time:
 *
 * - `needsStudentId`: no registration number has been entered, so there is nobody to
 *   ask about and no request is made.
 * - `loading`: the first request for this student is in flight.
 * - `error`: nothing to show and the request failed. It stays up while the next poll
 *   is in flight (it does not flip back to `loading`).
 * - `ready`: there is a list (possibly empty). `refreshError` is set while the latest
 *   refresh is failing, so the screen keeps the last list and says it may be out of date.
 */
export type MyHoldsState =
  | { kind: "needsStudentId" }
  | { kind: "loading" }
  | { kind: "error"; error: unknown }
  | { kind: "ready"; holds: MyHold[]; refreshError: unknown };

/**
 * GET /me/holds for the registration number the student entered, refreshed every
 * MY_HOLDS_POLL_MS. docs/FRONTEND_SPEC.md ("Resilience"): the screen is driven by that
 * saved number plus this request, never by anything held in memory.
 *
 * - **Gated on the number** (`enabled`): without it the backend could only answer 400,
 *   so no request goes out at all.
 * - **The number is in the query key.** The header changes when the student switches
 *   numbers, but TanStack Query only fetches again when the key changes; without it a
 *   student who switches would keep seeing the previous student's applications until
 *   the next poll. With it, the new number is a different query with no data, so the
 *   screen shows its loading state, never someone else's list.
 * - No retry: the next poll is the retry, so a failure shows at once and the screen
 *   recovers by itself when the backend answers again.
 */
export function useMyHolds(): MyHoldsState {
  const { studentId } = useStudentId();
  const query = useQuery({
    queryKey: myHoldsQueryKey(studentId),
    queryFn: ({ signal }) => listMyHolds(signal),
    enabled: studentId !== null,
    refetchInterval: MY_HOLDS_POLL_MS,
    retry: false,
  });

  // With no data yet, TanStack Query clears `error` each time a refetch starts, so while
  // the backend is down the screen would flip error -> loading -> error on every poll.
  // Remember the last error until a request succeeds (the same device as useClassList
  // and useHold).
  const [lastError, setLastError] = useState<unknown>(null);
  useEffect(() => {
    if (query.isError) {
      setLastError(query.error);
    } else if (query.isSuccess) {
      setLastError(null);
    }
  }, [query.isError, query.isSuccess, query.error]);

  if (studentId === null) {
    return { kind: "needsStudentId" };
  }

  const failing = query.errorUpdateCount > 0 && !query.isSuccess;
  const error = query.error ?? lastError;

  if (query.data === undefined) {
    return failing ? { kind: "error", error } : { kind: "loading" };
  }
  return { kind: "ready", holds: query.data, refreshError: failing ? error : null };
}
