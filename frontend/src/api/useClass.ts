import { useQuery } from "@tanstack/react-query";
import { getClass } from "./endpoints";

/** Cache key of one class (not the class list: that is `classListQueryKey`). */
export function classQueryKey(classId: string) {
  return ["class", classId] as const;
}

/**
 * GET /classes/{classId}: the class's name, for screens that only have its id
 * (a hold carries `classId` and nothing else). A class's name does not change
 * while a student looks at a hold, so it is fetched once and kept for a minute.
 * No retry: a screen that cannot get the name shows the id instead.
 */
export function useClass(classId: string) {
  return useQuery({
    queryKey: classQueryKey(classId),
    queryFn: ({ signal }) => getClass(classId, signal),
    staleTime: 60_000,
    retry: false,
  });
}
