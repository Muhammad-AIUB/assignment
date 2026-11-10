import { useEffect, useState } from "react";
import axios from "axios";
import { useQuery } from "@tanstack/react-query";
import { getHold } from "./endpoints";
import type { Hold, HoldStatus } from "./types";

/** Cache key of one hold. The confirm and cancel mutations write to and invalidate it. */
export function holdQueryKey(holdId: string) {
  return ["hold", holdId] as const;
}

/** docs/FRONTEND_SPEC.md: poll GET /holds/:holdId every few seconds. */
export const HOLD_POLL_MS = 4000;

/** Statuses the backend never leaves, so there is nothing more to poll for. */
const FINAL_STATUSES: readonly HoldStatus[] = ["CONFIRMED", "EXPIRED", "CANCELLED"];

/** True for a 404: this hold does not exist, and asking again will not change that. */
export function isNotFoundError(error: unknown): boolean {
  return axios.isAxiosError(error) && error.response?.status === 404;
}

/**
 * What the status screen has to draw. Exactly one of these at a time:
 *
 * - `loading`: the first request is in flight and nothing has ever come back.
 * - `error`: nothing to show and the request failed. `notFound` is the 404 case.
 *   Anything else is retried by the next poll.
 * - `ready`: there is a hold to draw. `fetchedAt` is when, on this device's clock,
 *   the latest answer arrived (the countdown needs it, see resolveHold).
 *   `refreshError` is set while the latest refresh is failing, so the screen can
 *   say its picture may be out of date.
 */
export type HoldState =
  | { kind: "loading" }
  | { kind: "error"; error: unknown; notFound: boolean }
  | { kind: "ready"; hold: Hold; fetchedAt: number; refreshError: unknown };

/**
 * GET /holds/:holdId, refreshed every HOLD_POLL_MS. Polling pauses in a hidden tab
 * and catches up when it is visible again (TanStack Query defaults). It stops for
 * good once the server reports a status that cannot change, and on a 404.
 *
 * No retry: the next poll is the retry, so a failure shows at once.
 */
export function useHold(holdId: string): HoldState {
  const query = useQuery({
    queryKey: holdQueryKey(holdId),
    queryFn: ({ signal }) => getHold(holdId, signal),
    refetchInterval: (q) => {
      const hold = q.state.data;
      if (hold !== undefined && FINAL_STATUSES.includes(hold.status)) return false;
      if (isNotFoundError(q.state.error)) return false;
      return HOLD_POLL_MS;
    },
    retry: false,
  });

  // With no data yet, TanStack Query clears `error` each time a refetch starts, so
  // while the backend is down the screen would flip error -> loading -> error on
  // every poll. Remember the last error until a request succeeds (the same device
  // as useClassList).
  const [lastError, setLastError] = useState<unknown>(null);
  useEffect(() => {
    if (query.isError) {
      setLastError(query.error);
    } else if (query.isSuccess) {
      setLastError(null);
    }
  }, [query.isError, query.isSuccess, query.error]);

  const failing = query.errorUpdateCount > 0 && !query.isSuccess;
  const error = query.error ?? lastError;

  if (query.data === undefined) {
    return failing
      ? { kind: "error", error, notFound: isNotFoundError(error) }
      : { kind: "loading" };
  }
  return {
    kind: "ready",
    hold: query.data,
    fetchedAt: query.dataUpdatedAt,
    refreshError: failing ? error : null,
  };
}
