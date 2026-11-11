import react from "@vitejs/plugin-react";
import { defineConfig } from "vitest/config";

// Kept apart from vite.config.ts so the production build never reads test
// settings. `npm test` runs once (vitest run); `npm run test:watch` watches.
export default defineConfig({
  plugins: [react()],
  test: {
    environment: "jsdom",
    setupFiles: ["./src/test/setup.ts"],
    include: ["src/**/*.test.{ts,tsx}"],
    // Explicit imports in every test file (no hidden globals), so the same
    // files typecheck without a "vitest/globals" types entry.
    globals: false,
    // The 5 s default is too tight for a first jsdom render on a loaded machine.
    testTimeout: 40_000,
    // The default is one worker per core. On a laptop that is also running Docker
    // all of them start at once, and Vitest gives up on a worker that has not
    // answered within 60 s ("Timeout waiting for worker to respond"): every file
    // then fails with "no tests". Two workers start reliably, and the suite is
    // dominated by importing jsdom and React, not by running tests.
    maxWorkers: 2,
  },
});
