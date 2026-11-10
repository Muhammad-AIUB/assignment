import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useNavigate } from "react-router-dom";
import { createHold } from "./endpoints";
import { classListQueryKey } from "./useClassList";

/** Application status screen of a hold (the `/holds/:holdId` route). */
export function holdPath(holdId: string): string {
  return `/holds/${encodeURIComponent(holdId)}`;
}

/**
 * The apply flow: POST /classes/{classId}/holds for the class passed to
 * `mutate`.
 *
 * - 201: go to the application status screen for the `holdId` the backend
 *   returned.
 * - Any failure (409 no seat / already applied, 404, 400 and so on) is left on
 *   the returned mutation as `error`, and the caller draws it on the class
 *   list. Nothing navigates, and the message is the backend's own.
 *
 * The class list is invalidated whether the request succeeded or not, so seat
 * counts are refreshed straight away instead of at the next 4 s poll: after a
 * success a seat is held, and after a "no seat" 409 the list was out of date
 * and the button should now show Full.
 *
 * No retry (mutations do not retry by default): re-sending a POST that creates
 * a hold is not something to do silently.
 */
export function useApplyForClass() {
  const queryClient = useQueryClient();
  const navigate = useNavigate();

  return useMutation({
    mutationFn: (classId: string) => createHold(classId),
    onSuccess: (hold) => {
      navigate(holdPath(hold.holdId));
    },
    onSettled: () => {
      void queryClient.invalidateQueries({ queryKey: classListQueryKey });
    },
  });
}
