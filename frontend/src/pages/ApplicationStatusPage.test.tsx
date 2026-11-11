import { screen, within } from "@testing-library/react";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import type { HoldStatus } from "../api/types";
import { aClass, aHold, serveClass } from "../test/fixtures";
import { renderWithProviders } from "../test/renderWithProviders";
import { API_BASE, server } from "../test/server";

const ROUTE = "/holds/h_1";

function serveHold(status: HoldStatus) {
  server.use(
    http.get(`${API_BASE}/holds/h_1`, () => HttpResponse.json(aHold({ status }))),
  );
}

describe("application status: what the student sees", () => {
  it("shows the status as a plain label, the class name and the time left", async () => {
    serveHold("HELD");
    serveClass(aClass({ name: "Class 6 - Morning Shift" }));
    renderWithProviders(undefined, { route: ROUTE });

    expect(await screen.findByRole("heading", { name: "Your seat is on hold" })).toBeInTheDocument();
    expect(screen.getAllByText("On hold").length).toBeGreaterThan(0);
    // The class is named, not just identified by its id.
    expect(await screen.findByText("Class 6 - Morning Shift")).toBeInTheDocument();
    expect(screen.getByRole("timer", { name: "Time left to pay the admission fee" })).toHaveTextContent(
      /^\d{2}:\d{2}$/,
    );
  });

  it("falls back to the class id while the class name is not available", async () => {
    serveHold("HELD");
    server.use(
      http.get(`${API_BASE}/classes/c_1`, () =>
        HttpResponse.json({ error: "CLASS_NOT_FOUND", message: "Class not found." }, { status: 404 }),
      ),
    );
    renderWithProviders(undefined, { route: ROUTE });

    await screen.findByRole("heading", { name: "Your seat is on hold" });
    const klass = screen.getByText("Class", { selector: "dt" }).parentElement;
    expect(klass).not.toBeNull();
    expect(within(klass as HTMLElement).getByText("c_1")).toBeInTheDocument();
  });

  it.each([
    ["CONFIRMED", "Admission confirmed"],
    ["EXPIRED", "This hold has expired"],
    ["CANCELLED", "Application cancelled"],
  ] as const)("shows a %s hold as: %s", async (status, title) => {
    serveHold(status);
    serveClass();
    renderWithProviders(undefined, { route: ROUTE });

    expect(await screen.findByRole("heading", { name: title })).toBeInTheDocument();
    // Nothing is left to count down to or to pay.
    expect(screen.queryByRole("timer")).not.toBeInTheDocument();
    expect(screen.getByRole("button", { name: "ভর্তি ফি দিন" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "বাতিল" })).toBeDisabled();
  });
});
