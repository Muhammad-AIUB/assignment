import { useEffect, useState } from "react";
import { useQuery } from "@tanstack/react-query";
import { listClasses } from "./endpoints";
import type { SchoolClass } from "./types";

/** Other screens that change seat counts invalidate this key to refresh now. */
export const classListQueryKey = ["classes"] as const;

/** docs/FRONTEND_SPEC.md: poll GET /classes every 3-5 seconds. */
export const CLASS_LIST_POLL_MS = 4000;

/**
 * What the class list screen has to draw. Exactly one of these at a time:
 *
 * - `loading`: the very first request is in flight and nothing has ever come
 *   back. This is the only time the screen shows a loading state.
 * - `error`: nothing to show yet and the request failed. It stays up (it does
 *   not flip back to `loading`) while the next poll is in flight.
 * - `ready`: there is data to draw. `refreshError` is set when the latest
 *   refresh failed, so the screen keeps the last known numbers and says so.
 */
export type ClassListState =
  | { kind: "loading" }
  | { kind: "error"; error: unknown }
  | { kind: "ready"; classes: SchoolClass[]; refreshError: unknown };

/**
 * GET /classes, refreshed on its own every CLASS_LIST_POLL_MS (no manual
 * refresh). Polling pauses while the tab is hidden and a refetch happens as
 * soon as it is visible again (TanStack Query defaults).
 *
 * No retry: the next poll is the retry, so an error shows up straight away
 * instead of after the default three backed-off attempts, and recovery is
 * automatic once the backend answers again.
 */
export function useClassList(): ClassListState {
  const query = useQuery({
    queryKey: classListQueryKey,
    queryFn: ({ signal }) => listClasses(signal),
    refetchInterval: CLASS_LIST_POLL_MS,
    retry: false,
  });

  // With no data yet, TanStack Query resets the query to "pending" and clears
  // `error` every time a refetch starts. Without this the screen would flip
  // error -> loading -> error on every poll while the backend is down, which is
  // exactly the full-page flicker the first-load-only spinner is meant to avoid.
  // So remember the last error until a request succeeds.
  const [lastError, setLastError] = useState<unknown>(null);
  useEffect(() => {
    if (query.isError) {
      setLastError(query.error);
    } else if (query.isSuccess) {
      setLastError(null);
    }
  }, [query.isError, query.isSuccess, query.error]);

  // True from a failure until the next success, including while the retrying
  // request is in flight (status is "pending" again then, but the count is not).
  const failing = query.errorUpdateCount > 0 && !query.isSuccess;
  const error = query.error ?? lastError;

  if (query.data === undefined) {
    return failing ? { kind: "error", error } : { kind: "loading" };
  }
  return {
    kind: "ready",
    classes: query.data,
    refreshError: failing ? error : null,
  };
}
