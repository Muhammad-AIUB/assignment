import { act, screen } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { Hold, HoldStatus } from "../api/types";
import { aClass, aHold, serveClass } from "../test/fixtures";
import { renderWithProviders } from "../test/renderWithProviders";
import { API_BASE, server } from "../test/server";

// The clock is faked, never slept on. `shouldAdvanceTime` keeps the fake clock
// moving with real time so mocked requests still complete; where the exact
// second matters the test pins the clock with setSystemTime first.

const T0 = Date.UTC(2026, 10, 11, 10, 0, 0);
const ROUTE = "/holds/h_1";
const iso = (ms: number) => new Date(ms).toISOString();

/** Serves GET /holds/h_1 from a mutable hold and counts the requests it gets. */
function serveHold(initial: Hold) {
  const state = { hold: initial, requests: 0 };
  server.use(
    http.get(`${API_BASE}/holds/h_1`, () => {
      state.requests += 1;
      return HttpResponse.json(state.hold);
    }),
  );
  serveClass(aClass());
  return state;
}

/** Moves the fake clock so that a one-second tick lands exactly on T0 + offsetMs. */
function tickTo(offsetMs: number) {
  act(() => {
    vi.setSystemTime(T0 + offsetMs - 1_000);
    vi.advanceTimersByTime(1_000);
  });
}

async function openHold(initial: Hold) {
  const state = serveHold(initial);
  renderWithProviders(undefined, { route: ROUTE });
  await screen.findByRole("heading", { name: /./ });
  return state;
}

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true });
  vi.setSystemTime(T0);
});

afterEach(() => {
  vi.useRealTimers();
});

describe("application status: the clock", () => {
  it("counts the time left down every second", async () => {
    // 90.5 s from now, so every whole second shown is unambiguous.
    await openHold(aHold({ expiresAt: iso(T0 + 90_500) }));
    const timer = screen.getByRole("timer", { name: "Time left to pay the admission fee" });

    tickTo(1_000);
    expect(timer).toHaveTextContent("01:30");
    tickTo(2_000);
    expect(timer).toHaveTextContent("01:29");
    tickTo(31_000);
    expect(timer).toHaveTextContent("01:00");
    tickTo(32_000);
    expect(timer).toHaveTextContent("00:59");
  });

  it("flips to EXPIRED the moment the countdown reaches zero, with no request and no reload", async () => {
    const state = await openHold(aHold({ expiresAt: iso(T0 + 2_500) }));
    const requestsBefore = state.requests;

    // One tick that crosses the deadline. Checked at once, before anything can be awaited.
    tickTo(3_000);

    expect(screen.getByRole("heading", { name: "This hold has expired" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "ভর্তি ফি দিন" })).toBeDisabled();
    expect(state.requests).toBe(requestsBefore);
  });

  it("asks the server again every few seconds, so a change made elsewhere shows up", async () => {
    const state = await openHold(aHold());
    expect(screen.getByRole("heading", { name: "Your seat is on hold" })).toBeInTheDocument();

    // Cancelled in another tab.
    state.hold = aHold({ status: "CANCELLED" });
    await act(async () => {
      await vi.advanceTimersByTimeAsync(5_000);
    });

    expect(await screen.findByRole("heading", { name: "Application cancelled" })).toBeInTheDocument();
    expect(state.requests).toBeGreaterThanOrEqual(2);
  });

  it.each(["CONFIRMED", "EXPIRED", "CANCELLED"] as const satisfies readonly HoldStatus[])(
    "stops asking once the server reports %s, which can no longer change",
    async (status) => {
      const state = await openHold(aHold({ status }));
      const requestsBefore = state.requests;

      await act(async () => {
        await vi.advanceTimersByTimeAsync(30_000);
      });

      expect(state.requests).toBe(requestsBefore);
    },
  );
});
