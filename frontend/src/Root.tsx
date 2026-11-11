import { QueryClientProvider } from "@tanstack/react-query";
import App from "./App";
import ErrorBoundary from "./components/ErrorBoundary";
import { StudentIdProvider } from "./identity/StudentIdProvider";
import { queryClient } from "./queryClient";

/**
 * The whole app with its providers, behind a last-resort error boundary.
 *
 * The boundary in AppRoutes protects each screen and keeps the header on screen. This
 * one covers what that cannot: a crash in the header itself or in anything above the
 * screens. Without it such a crash unmounts the whole tree and leaves an empty page.
 * It sits outside every provider on purpose, and its fallback needs none of them.
 */
export default function Root() {
  return (
    <ErrorBoundary>
      <QueryClientProvider client={queryClient}>
        <StudentIdProvider>
          <App />
        </StudentIdProvider>
      </QueryClientProvider>
    </ErrorBoundary>
  );
}
