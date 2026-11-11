import { vi, type MockInstance } from "vitest";

/**
 * For a test that makes a component throw while rendering on purpose (to check what
 * an error boundary does with it).
 *
 * Two things would otherwise bury the test output in stack traces: React prints every
 * error a boundary catches with console.error, and in development React throws it
 * through a fake DOM event, which jsdom reports as an "uncaught error" on the window
 * unless the event is cancelled. Both are silenced here. `consoleError` is returned so
 * a test can still look at what was logged; call `restore()` in afterEach.
 */
export function expectCrash(): { consoleError: MockInstance<typeof console.error>; restore: () => void } {
  const consoleError = vi.spyOn(console, "error").mockImplementation(() => {});
  const swallow = (event: ErrorEvent) => event.preventDefault();
  window.addEventListener("error", swallow);
  return {
    consoleError,
    restore() {
      window.removeEventListener("error", swallow);
      consoleError.mockRestore();
    },
  };
}
