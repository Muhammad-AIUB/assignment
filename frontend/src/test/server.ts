import { setupServer } from "msw/node";

/** Base URL the tests point the app at (see setup.ts). */
export const API_BASE = "http://api.test";

/**
 * The fake backend. Only the network is mocked: the app's own modules run for
 * real. Handlers are added per test with `server.use(...)` and removed again
 * after each one; a request nobody handled fails the test (setup.ts).
 */
export const server = setupServer();
