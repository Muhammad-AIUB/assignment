import { useQueryClient } from "@tanstack/react-query";
import type { ReactNode } from "react";
import { Route, Routes, useLocation } from "react-router-dom";
import AppLayout from "./components/AppLayout";
import ErrorBoundary from "./components/ErrorBoundary";
import ClassListPage from "./pages/ClassListPage";
import CreateClassPage from "./pages/CreateClassPage";
import ApplicationStatusPage from "./pages/ApplicationStatusPage";
import MyApplicationsPage from "./pages/MyApplicationsPage";
import NotFoundPage from "./pages/NotFoundPage";

/**
 * Catches a crash in whichever screen is showing, inside the shell, so the header and
 * the registration number input stay on screen with the message.
 *
 * - Going to another page (the path changes) clears the message.
 * - "Try again" first throws away everything the app has cached. A screen usually
 *   crashes on data it was given (a response in an unexpected shape); the cache would
 *   hand the very same data to the screen again, and it would fail again at once.
 *   With the cache empty the screen starts from a fresh request.
 */
function ScreenBoundary({ children }: { children: ReactNode }) {
  const { pathname } = useLocation();
  const queryClient = useQueryClient();
  return (
    <ErrorBoundary resetKey={pathname} onReset={() => queryClient.removeQueries()}>
      {children}
    </ErrorBoundary>
  );
}

/**
 * The shell and the routes, without a router around them. <App> puts a
 * BrowserRouter around this; the tests put a MemoryRouter around the very same
 * routes (src/test/renderWithProviders.tsx), so a test exercises the real
 * route table instead of a copy of it.
 */
export default function AppRoutes() {
  return (
    <AppLayout>
      <ScreenBoundary>
        <Routes>
          <Route path="/" element={<ClassListPage />} />
          <Route path="/admin/new-class" element={<CreateClassPage />} />
          <Route path="/holds/:holdId" element={<ApplicationStatusPage />} />
          <Route path="/me" element={<MyApplicationsPage />} />
          <Route path="*" element={<NotFoundPage />} />
        </Routes>
      </ScreenBoundary>
    </AppLayout>
  );
}
