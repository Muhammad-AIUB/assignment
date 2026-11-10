import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import type { SchoolClass } from "../api/types";
import { renderWithProviders } from "../test/renderWithProviders";
import { API_BASE, server } from "../test/server";

// Characterization tests: they lock in how the class list (Sessions 13-15)
// behaves today, so the shared code it uses can be refactored under them.

const morningClass: SchoolClass = {
  id: "c_1",
  name: "Class 6 - Morning Shift",
  totalSeats: 120,
  heldSeats: 15,
  confirmedSeats: 80,
  availableSeats: 25,
};

const fullClass: SchoolClass = {
  id: "c_2",
  name: "Class 7 - Day Shift",
  totalSeats: 40,
  heldSeats: 10,
  confirmedSeats: 30,
  availableSeats: 0,
};

function serveClasses(classes: SchoolClass[]) {
  server.use(http.get(`${API_BASE}/classes`, () => HttpResponse.json(classes)));
}

/** The card (list item) that holds the heading of the class with this name. */
async function findCard(name: RegExp) {
  const heading = await screen.findByRole("heading", { name });
  const card = heading.closest("li");
  if (card === null) throw new Error(`no card around heading ${name}`);
  return card;
}

describe("class list", () => {
  it("shows each class with its total, held, confirmed and available seat counts", async () => {
    serveClasses([morningClass]);
    renderWithProviders();

    const card = await findCard(/Class 6.*Morning Shift/);

    expect(within(card).getByText("seats left")).toHaveTextContent("seats left of 120");
    expect(within(card).getByText("confirmed")).toHaveTextContent("80 confirmed");
    expect(within(card).getByText("on hold")).toHaveTextContent("15 on hold");
    expect(within(card).getByText("available")).toHaveTextContent("25 available");
  });

  it("disables Apply when no seat is available, and keeps it enabled otherwise", async () => {
    serveClasses([morningClass, fullClass]);
    renderWithProviders();

    const open = await screen.findByRole("button", {
      name: "আবেদন করুন: Class 6 - Morning Shift",
    });
    const full = await screen.findByRole("button", {
      name: "আবেদন করুন: Class 7 - Day Shift",
    });

    expect(open).toBeEnabled();
    expect(full).toBeDisabled();
  });

  it("shows the backend's message in an alert when applying fails with a 409", async () => {
    serveClasses([morningClass]);
    server.use(
      http.post(`${API_BASE}/classes/c_1/holds`, () =>
        HttpResponse.json(
          { error: "NO_SEAT_AVAILABLE", message: "No seats are available in this class." },
          { status: 409 },
        ),
      ),
    );
    const user = userEvent.setup();
    renderWithProviders(undefined, { studentId: "20152691234567890" });

    await user.click(
      await screen.findByRole("button", { name: "আবেদন করুন: Class 6 - Morning Shift" }),
    );

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "No seats are available in this class.",
    );
    // The student stays on the class list and can try again.
    expect(screen.getByRole("heading", { name: "Choose a class" })).toBeInTheDocument();
    expect(
      screen.getByRole("button", { name: "আবেদন করুন: Class 6 - Morning Shift" }),
    ).toBeEnabled();
  });
});
