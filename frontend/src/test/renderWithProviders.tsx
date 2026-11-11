import type { ReactElement } from "react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { render } from "@testing-library/react";
import { MemoryRouter } from "react-router-dom";
import AppRoutes from "../AppRoutes";
import { StudentIdProvider } from "../identity/StudentIdProvider";
import { STUDENT_ID_STORAGE_KEY } from "../identity/studentIdStore";

export interface RenderOptions {
  /** Where the MemoryRouter starts, for example "/holds/h_1". Default "/". */
  route?: string;
  /**
   * A registration number already saved in localStorage, as if the student had
   * entered it on an earlier visit. Left out: nothing is saved.
   */
  studentId?: string;
  /**
   * Use this client instead of the default one. The default throws a query's data away
   * the moment nothing shows it (gcTime 0), which is tidy for most tests but hides any
   * bug that comes from data the app has cached; pass a client with the production
   * cache time to test those.
   */
  queryClient?: QueryClient;
}

/**
 * Renders `ui` (default: the real shell and routes) inside the same providers
 * main.tsx uses, with a fresh QueryClient per call (no retries, nothing kept
 * after unmount) and the identity seeded through localStorage, which is what a
 * returning student has.
 */
export function renderWithProviders(
  ui: ReactElement = <AppRoutes />,
  { route = "/", studentId, queryClient: ownClient }: RenderOptions = {},
) {
  if (studentId !== undefined) {
    window.localStorage.setItem(STUDENT_ID_STORAGE_KEY, studentId);
  }
  const queryClient =
    ownClient ??
    new QueryClient({
      defaultOptions: { queries: { retry: false, gcTime: 0 } },
    });
  const result = render(
    <QueryClientProvider client={queryClient}>
      <StudentIdProvider>
        <MemoryRouter initialEntries={[route]}>{ui}</MemoryRouter>
      </StudentIdProvider>
    </QueryClientProvider>,
  );
  return { ...result, queryClient };
}
