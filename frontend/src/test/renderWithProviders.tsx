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
}

/**
 * Renders `ui` (default: the real shell and routes) inside the same providers
 * main.tsx uses, with a fresh QueryClient per call (no retries, nothing kept
 * after unmount) and the identity seeded through localStorage, which is what a
 * returning student has.
 */
export function renderWithProviders(
  ui: ReactElement = <AppRoutes />,
  { route = "/", studentId }: RenderOptions = {},
) {
  if (studentId !== undefined) {
    window.localStorage.setItem(STUDENT_ID_STORAGE_KEY, studentId);
  }
  const queryClient = new QueryClient({
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
