import { useMutation, useQueryClient } from "@tanstack/react-query";
import { createClass } from "./endpoints";
import type { CreateClassRequest } from "./types";
import { classListQueryKey } from "./useClassList";

/**
 * The create-class flow: POST /classes with the body passed to `mutate`.
 *
 * - 201: the created class is on the returned mutation as `data`. The class
 *   list is refreshed so the new class is already there when the screen
 *   redirects to it. `refetchType: "all"` matters: the list screen is not
 *   mounted while this one is, so its query has no observer, and the default
 *   (active queries only) would only mark it stale and leave the old list to
 *   show for a moment on arrival.
 * - Any failure (400 from the backend's validation, a network error, ...) is
 *   left on the mutation as `error`; the page draws it and stays where it is.
 *
 * No retry (mutations do not retry by default): re-sending a POST that creates
 * a class would create a second one if the first had in fact gone through.
 */
export function useCreateClass() {
  const queryClient = useQueryClient();

  return useMutation({
    mutationFn: (body: CreateClassRequest) => createClass(body),
    onSuccess: () => {
      // Not returned: the success message must not wait for the list refetch.
      void queryClient.invalidateQueries({
        queryKey: classListQueryKey,
        refetchType: "all",
      });
    },
  });
}
