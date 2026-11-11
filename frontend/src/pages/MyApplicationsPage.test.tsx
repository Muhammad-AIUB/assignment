import { screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import type { HoldStatus, MyHold } from "../api/types";
import { aClass, aHold, aMyHold, apiError, serveClass, STUDENT_ID } from "../test/fixtures";
import { renderWithProviders } from "../test/renderWithProviders";
import { API_BASE, server } from "../test/server";

const ROUTE = "/me";
/** Another student: a different valid 17-digit registration number. */
const OTHER_STUDENT_ID = "20159998887776665";

/**
 * Serves GET /me/holds, answering from the X-Student-Id the request carries, and
 * records that header for every request (null when the request had none).
 */
function serveMyHolds(respond: (studentId: string | null) => MyHold[] | Promise<MyHold[]>) {
  const asked: Array<string | null> = [];
  server.use(
    http.get(`${API_BASE}/me/holds`, async ({ request }) => {
      const studentId = request.headers.get("x-student-id");
      asked.push(studentId);
      return HttpResponse.json(await respond(studentId));
    }),
  );
  return asked;
}

const FOUR_APPLICATIONS: MyHold[] = [
  aMyHold({ holdId: "h_4", classId: "c_4", className: "Class 9 - Evening Shift", status: "HELD" }),
  aMyHold({ holdId: "h_3", classId: "c_3", className: "Class 8 - Day Shift", status: "CONFIRMED" }),
  aMyHold({ holdId: "h_2", classId: "c_2", className: "Class 7 - Day Shift", status: "EXPIRED" }),
  aMyHold({ holdId: "h_1", classId: "c_1", className: "Class 6 - Morning Shift", status: "CANCELLED" }),
];

/** The list item that holds the heading of the application for the class with this name. */
async function findRow(className: string) {
  const heading = await screen.findByRole("heading", { name: className });
  const row = heading.closest("li");
  if (row === null) throw new Error(`no list item around the heading ${className}`);
  return row;
}

describe("my applications (TC-F10)", () => {
  it("asks the backend for this student's applications as soon as the screen opens", async () => {
    const asked = serveMyHolds(() => []);
    renderWithProviders(undefined, { route: ROUTE, studentId: STUDENT_ID });

    await waitFor(() => expect(asked.length).toBeGreaterThan(0));
    // Every request carries the saved registration number, none goes out without it.
    expect(asked.every((id) => id === STUDENT_ID)).toBe(true);
  });

  it("lists each application with its class, a plain status label and a link to its status screen", async () => {
    serveMyHolds(() => FOUR_APPLICATIONS);
    renderWithProviders(undefined, { route: ROUTE, studentId: STUDENT_ID });

    const expected: Array<[string, string, string]> = [
      ["Class 9 - Evening Shift", "On hold", "/holds/h_4"],
      ["Class 8 - Day Shift", "Confirmed", "/holds/h_3"],
      ["Class 7 - Day Shift", "Expired", "/holds/h_2"],
      ["Class 6 - Morning Shift", "Cancelled", "/holds/h_1"],
    ];
    for (const [className, label, href] of expected) {
      const row = await findRow(className);
      expect(within(row).getByText(label)).toBeInTheDocument();
      // Told apart by class name, so a screen reader can tell the links apart too.
      expect(
        within(row).getByRole("link", { name: `View status of ${className}` }),
      ).toHaveAttribute("href", href);
    }
  });

  it("tells a hold that is still running when it ends, and says nothing of the sort for the others", async () => {
    serveMyHolds(() => FOUR_APPLICATIONS);
    renderWithProviders(undefined, { route: ROUTE, studentId: STUDENT_ID });

    expect(within(await findRow("Class 9 - Evening Shift")).getByText(/Hold ends/)).toBeInTheDocument();
    for (const className of ["Class 8 - Day Shift", "Class 7 - Day Shift", "Class 6 - Morning Shift"]) {
      expect(within(await findRow(className)).queryByText(/Hold ends/)).not.toBeInTheDocument();
    }
  });

  it("keeps the order the backend sent (newest first)", async () => {
    serveMyHolds(() => FOUR_APPLICATIONS);
    renderWithProviders(undefined, { route: ROUTE, studentId: STUDENT_ID });

    await findRow("Class 9 - Evening Shift");
    const names = within(screen.getByRole("list", { name: "Your applications" }))
      .getAllByRole("heading", { level: 2 })
      .map((heading) => heading.textContent);
    expect(names).toEqual([
      "Class 9 - Evening Shift",
      "Class 8 - Day Shift",
      "Class 7 - Day Shift",
      "Class 6 - Morning Shift",
    ]);
  });

  it("opens the application's status screen from its link", async () => {
    serveMyHolds(() => [aMyHold()]);
    server.use(
      http.get(`${API_BASE}/holds/h_1`, () => HttpResponse.json(aHold({ status: "HELD" }))),
    );
    serveClass(aClass());
    const user = userEvent.setup();
    renderWithProviders(undefined, { route: ROUTE, studentId: STUDENT_ID });

    await user.click(
      await screen.findByRole("link", { name: "View status of Class 6 - Morning Shift" }),
    );

    expect(await screen.findByRole("heading", { name: "Your seat is on hold" })).toBeInTheDocument();
  });

  it("says so, with a way to the classes, when the student has not applied yet", async () => {
    serveMyHolds(() => []);
    renderWithProviders(undefined, { route: ROUTE, studentId: STUDENT_ID });

    expect(await screen.findByText("You have not applied to any class yet.")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: /Browse classes/ })).toHaveAttribute("href", "/");
    expect(screen.queryByRole("list", { name: "Your applications" })).not.toBeInTheDocument();
  });

  it("shows a loading state until the first answer arrives", async () => {
    let answer!: () => void;
    const gate = new Promise<void>((resolve) => {
      answer = resolve;
    });
    serveMyHolds(async () => {
      await gate;
      return [aMyHold()];
    });
    renderWithProviders(undefined, { route: ROUTE, studentId: STUDENT_ID });

    expect(await screen.findByText("Loading your applications…")).toBeInTheDocument();
    answer();
    expect(await findRow("Class 6 - Morning Shift")).toBeInTheDocument();
    expect(screen.queryByText("Loading your applications…")).not.toBeInTheDocument();
  });

  it("shows the backend's own message in an alert when the request is refused", async () => {
    server.use(
      http.get(`${API_BASE}/me/holds`, () =>
        apiError(400, "INVALID_STUDENT_ID", "X-Student-Id header is required and must be exactly 17 digits."),
      ),
    );
    renderWithProviders(undefined, { route: ROUTE, studentId: STUDENT_ID });

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "X-Student-Id header is required and must be exactly 17 digits.",
    );
    expect(screen.queryByRole("list", { name: "Your applications" })).not.toBeInTheDocument();
  });

  it("shows the generic message, never a blank screen, when the server cannot be reached", async () => {
    server.use(http.get(`${API_BASE}/me/holds`, () => HttpResponse.error()));
    renderWithProviders(undefined, { route: ROUTE, studentId: STUDENT_ID });

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("কিছু একটা ভুল হয়েছে, আবার চেষ্টা করুন");
    expect(alert).toHaveTextContent("Could not reach the server.");
  });

  it.each<[HoldStatus, string]>([
    ["HELD", "On hold"],
    ["CONFIRMED", "Confirmed"],
    ["EXPIRED", "Expired"],
    ["CANCELLED", "Cancelled"],
  ])("uses the same words for a %s application as the status screen does: %s", async (status, label) => {
    serveMyHolds(() => [aMyHold({ status })]);
    renderWithProviders(undefined, { route: ROUTE, studentId: STUDENT_ID });

    expect(within(await findRow("Class 6 - Morning Shift")).getByText(label)).toBeInTheDocument();
  });
});

describe("my applications with no registration number, or another one (TC-F13)", () => {
  it("asks for the registration number and shows no application, not even a placeholder list", async () => {
    serveMyHolds(() => FOUR_APPLICATIONS);
    renderWithProviders(undefined, { route: ROUTE });

    expect(
      await screen.findByText("Enter your registration number to see your applications."),
    ).toBeInTheDocument();
    expect(screen.queryByRole("list", { name: "Your applications" })).not.toBeInTheDocument();
    expect(screen.queryByText("Class 9 - Evening Shift")).not.toBeInTheDocument();
  });

  it("makes no request until a registration number is saved, then loads that student's applications", async () => {
    const asked = serveMyHolds((id) => (id === STUDENT_ID ? FOUR_APPLICATIONS : []));
    const user = userEvent.setup();
    renderWithProviders(undefined, { route: ROUTE });
    await screen.findByText("Enter your registration number to see your applications.");

    // On this very page, with no reload.
    await user.type(screen.getByLabelText("Registration number"), STUDENT_ID);
    await user.click(screen.getByRole("button", { name: "Save" }));

    expect(await findRow("Class 9 - Evening Shift")).toBeInTheDocument();
    // The first request ever made already carried the number: nothing went out without it.
    expect(asked.length).toBeGreaterThan(0);
    expect(asked.every((id) => id === STUDENT_ID)).toBe(true);
    expect(
      screen.queryByText("Enter your registration number to see your applications."),
    ).not.toBeInTheDocument();
  });

  it("never shows one student's applications to another after the number is switched", async () => {
    let release!: () => void;
    const gate = new Promise<void>((resolve) => {
      release = resolve;
    });
    const asked = serveMyHolds(async (id) => {
      if (id === STUDENT_ID) return [aMyHold({ holdId: "h_a", className: "Class A - Private" })];
      await gate; // the other student's answer is slow
      return [aMyHold({ holdId: "h_b", className: "Class B - Other student" })];
    });
    const user = userEvent.setup();
    renderWithProviders(undefined, { route: ROUTE, studentId: STUDENT_ID });
    await findRow("Class A - Private");

    const input = screen.getByLabelText("Registration number");
    await user.clear(input);
    await user.type(input, OTHER_STUDENT_ID);
    await user.click(screen.getByRole("button", { name: "Save" }));

    // The new student's request is out and unanswered: nothing of the first student may be on screen.
    await waitFor(() => expect(asked).toContain(OTHER_STUDENT_ID));
    expect(screen.queryByText("Class A - Private")).not.toBeInTheDocument();

    release();
    expect(await findRow("Class B - Other student")).toBeInTheDocument();
    expect(screen.queryByText("Class A - Private")).not.toBeInTheDocument();
  });
});
