import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "react-router-dom";
import { cancelHold, confirmHold } from "./endpoints";
import type { Hold } from "./types";
import { classListQueryKey } from "./useClassList";
import { holdQueryKey } from "./useHold";

/**
 * "ভর্তি ফি দিন": POST /holds/:holdId/confirm.
 *
 * On success the cached hold is set to the status the server just returned
 * (CONFIRMED), so the screen changes at once instead of showing an enabled
 * button until the refetch lands, and the hold is then refetched for the
 * server's own record. The same refetch runs after a failure: a 409 means the
 * hold is no longer what the screen thinks (expired, cancelled, already
 * confirmed), and the status shown should catch up. The class list is refreshed
 * too, since confirming moves a seat from held to confirmed.
 *
 * The failure stays on the returned mutation as `error` for the screen to draw.
 * No retry: confirming is not re-sent silently.
 */
export function useConfirmHold(holdId: string) {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: () => confirmHold(holdId),
    onSuccess: (confirmed) => {
      queryClient.setQueryData<Hold>(holdQueryKey(holdId), (hold) =>
        hold === undefined ? hold : { ...hold, status: confirmed.status },
      );
    },
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: holdQueryKey(holdId) });
      void queryClient.invalidateQueries({ queryKey: classListQueryKey });
    },
  });
}

/**
 * "বাতিল": DELETE /holds/:holdId.
 *
 * On success (204) the cached hold is marked CANCELLED, so going back to this
 * page does not flash a hold that is gone, and the student is sent to the class
 * list ("/"). On failure nothing navigates; the error stays on the mutation and
 * the hold is refetched, because a 409 means its real state is something else.
 * The class list is refreshed either way: a cancelled hold frees a seat.
 */
export function useCancelHold(holdId: string) {
  const queryClient = useQueryClient();
  const navigate = useNavigate();

  return useMutation({
    mutationFn: () => cancelHold(holdId),
    onSuccess: () => {
      queryClient.setQueryData<Hold>(holdQueryKey(holdId), (hold) =>
        hold === undefined ? hold : { ...hold, status: "CANCELLED" },
      );
      navigate("/");
    },
    onSettled: (_data, error) => {
      void queryClient.invalidateQueries({ queryKey: classListQueryKey });
      if (error !== null) {
        void queryClient.invalidateQueries({ queryKey: holdQueryKey(holdId) });
      }
    },
  });
}
