import { act, screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { MyHold } from "../api/types";
import { aMyHold, STUDENT_ID } from "../test/fixtures";
import { renderWithProviders } from "../test/renderWithProviders";
import { API_BASE, server } from "../test/server";

// The list refreshes itself, the way the class list and the status screen do. The
// clock is faked, never slept on (`shouldAdvanceTime` keeps mocked requests moving).

const ROUTE = "/me";

/** Serves GET /me/holds from state a test can change, failing while `failing` is set. */
function serveMyHolds(initial: MyHold[]) {
  const state = { holds: initial, failing: false, requests: 0 };
  server.use(
    http.get(`${API_BASE}/me/holds`, () => {
      state.requests += 1;
      return state.failing ? HttpResponse.error() : HttpResponse.json(state.holds);
    }),
  );
  return state;
}

async function findRow(className: string) {
  const heading = await screen.findByRole("heading", { name: className });
  const row = heading.closest("li");
  if (row === null) throw new Error(`no list item around the heading ${className}`);
  return row;
}

const advance = (ms: number) =>
  act(async () => {
    await vi.advanceTimersByTimeAsync(ms);
  });

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true });
});

afterEach(() => {
  vi.useRealTimers();
});

describe("my applications: refreshing", () => {
  it("asks again every few seconds, so a change made in another tab shows up with no reload", async () => {
    const state = serveMyHolds([aMyHold({ status: "HELD" })]);
    renderWithProviders(undefined, { route: ROUTE, studentId: STUDENT_ID });
    expect(within(await findRow("Class 6 - Morning Shift")).getByText("On hold")).toBeInTheDocument();

    // Confirmed on another device.
    state.holds = [aMyHold({ status: "CONFIRMED" })];
    await advance(5_000);

    await vi.waitFor(() =>
      expect(within(screen.getByRole("listitem")).getByText("Confirmed")).toBeInTheDocument(),
    );
    expect(state.requests).toBeGreaterThanOrEqual(2);
  });

  it("shows an application the student made in another tab", async () => {
    const state = serveMyHolds([aMyHold({ holdId: "h_1", className: "Class 6 - Morning Shift" })]);
    renderWithProviders(undefined, { route: ROUTE, studentId: STUDENT_ID });
    await findRow("Class 6 - Morning Shift");

    state.holds = [
      aMyHold({ holdId: "h_2", classId: "c_2", className: "Class 7 - Day Shift" }),
      ...state.holds,
    ];
    await advance(5_000);

    expect(await findRow("Class 7 - Day Shift")).toBeInTheDocument();
    expect(screen.getAllByRole("heading", { level: 2 })).toHaveLength(2);
  });

  it("keeps the last list on screen and says it may be out of date while a refresh fails, then recovers by itself", async () => {
    const state = serveMyHolds([aMyHold()]);
    renderWithProviders(undefined, { route: ROUTE, studentId: STUDENT_ID });
    await findRow("Class 6 - Morning Shift");

    state.failing = true;
    await advance(5_000);

    expect(await screen.findByText(/may be out of date/)).toBeInTheDocument();
    // The last answer is still there to read.
    expect(screen.getByRole("heading", { name: "Class 6 - Morning Shift" })).toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();

    state.failing = false;
    await advance(5_000);

    await vi.waitFor(() => expect(screen.queryByText(/may be out of date/)).not.toBeInTheDocument());
    expect(screen.getByRole("heading", { name: "Class 6 - Morning Shift" })).toBeInTheDocument();
  });

  it("does not flicker between the error and the loading state while the server stays down", async () => {
    const state = serveMyHolds([]);
    state.failing = true;
    renderWithProviders(undefined, { route: ROUTE, studentId: STUDENT_ID });
    await screen.findByRole("alert");

    // Several polls later it is still the error, and the loading text never came back.
    for (let i = 0; i < 3; i++) {
      await advance(4_100);
      expect(screen.getByRole("alert")).toBeInTheDocument();
      expect(screen.queryByText("Loading your applications…")).not.toBeInTheDocument();
    }

    state.failing = false;
    await advance(4_100);
    await vi.waitFor(() => expect(screen.queryByRole("alert")).not.toBeInTheDocument());
    expect(screen.getByText("You have not applied to any class yet.")).toBeInTheDocument();
  });
});
