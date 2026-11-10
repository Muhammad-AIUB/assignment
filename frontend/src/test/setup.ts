import "@testing-library/jest-dom/vitest";
import { cleanup, configure } from "@testing-library/react";
import { afterAll, afterEach, beforeAll, beforeEach } from "vitest";
import { resetStudentIdStoreForTests } from "../identity/studentIdStore";
import { API_BASE, server } from "./server";

// findBy* / waitFor give up after 1 s by default. On a loaded machine (a laptop
// building Docker images, a busy CI runner) the first render plus a mocked
// request can take longer than that; the tests still never sleep, they only
// wait up to this long for the screen to change.
configure({ asyncUtilTimeout: 15_000 });

// A request with no handler is a bug in the test or in the app: fail loudly
// instead of letting it hit the network.
beforeAll(() => server.listen({ onUnhandledRequest: "error" }));

beforeEach(() => {
  window.__APP_CONFIG__ = { apiBaseUrl: API_BASE };
});

afterEach(() => {
  cleanup();
  server.resetHandlers();
  window.localStorage.clear();
  // studentIdStore caches the id in a module variable: clearing localStorage
  // alone would leak the previous test's id into the next test.
  resetStudentIdStoreForTests();
  delete window.__APP_CONFIG__;
});

afterAll(() => server.close());
