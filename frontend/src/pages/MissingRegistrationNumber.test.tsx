import { screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { describe, expect, it } from "vitest";
import { aClass, aHold, apiError, serveClass, STUDENT_ID } from "../test/fixtures";
import { renderWithProviders } from "../test/renderWithProviders";
import { API_BASE, server } from "../test/server";

// Session 22, finding 5: acting without a registration number used to send the request
// anyway, and the student read the backend's technical answer, "X-Student-Id header is
// required and must be exactly 17 digits.", which names an HTTP header and says nothing
// about what to do. Now nothing is sent, and the screen says what to do and puts the
// cursor in the field to fill in.

const MESSAGE = "Enter your 17-digit registration number at the top of the page, then try again.";
const RAW_BACKEND_WORDING = "X-Student-Id header is required and must be exactly 17 digits.";

/** The backend's answer to an action sent with no registration number. */
const refused = () => apiError(400, "INVALID_STUDENT_ID", RAW_BACKEND_WORDING);

/** Records the X-Student-Id of each request that reaches `respond`'s route (null: none was sent). */
function recorder() {
  const sent: Array<string | null> = [];
  return { sent, note: (request: Request) => void sent.push(request.headers.get("x-student-id")) };
}

describe("apply without a registration number", () => {
  const APPLY = "আবেদন করুন: Class 6 - Morning Shift";

  function serveClassList() {
    server.use(http.get(`${API_BASE}/classes`, () => HttpResponse.json([aClass()])));
  }

  it("sends nothing, says what to do in plain words, and puts the cursor in the registration field", async () => {
    const user = userEvent.setup();
    const applied = recorder();
    serveClassList();
    server.use(
      http.post(`${API_BASE}/classes/c_1/holds`, ({ request }) => {
        applied.note(request);
        return refused();
      }),
    );
    renderWithProviders();

    await user.click(await screen.findByRole("button", { name: APPLY }));

    expect(await screen.findByRole("alert")).toHaveTextContent(MESSAGE);
    // Not the backend's technical wording, and no request was made to get it.
    expect(document.body).not.toHaveTextContent("X-Student-Id");
    expect(applied.sent).toEqual([]);
    expect(screen.getByLabelText("Registration number")).toHaveFocus();
    // The student is still on the list and can try again.
    expect(screen.getByRole("heading", { name: "Choose a class" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: APPLY })).toBeEnabled();
  });

  it("drops the message by itself once a number is saved, and the next Apply goes out with that number", async () => {
    const user = userEvent.setup();
    const applied = recorder();
    serveClassList();
    server.use(
      http.post(`${API_BASE}/classes/c_1/holds`, ({ request }) => {
        applied.note(request);
        return apiError(409, "NO_SEAT_AVAILABLE", "No seats are available in this class.");
      }),
    );
    renderWithProviders();
    await user.click(await screen.findByRole("button", { name: APPLY }));
    await screen.findByText(MESSAGE);

    await user.type(screen.getByLabelText("Registration number"), STUDENT_ID);
    await user.click(screen.getByRole("button", { name: "Save" }));
    expect(screen.queryByText(MESSAGE)).not.toBeInTheDocument();

    await user.click(screen.getByRole("button", { name: APPLY }));
    // Now it is a real answer from the backend, in the backend's words.
    expect(await screen.findByText("No seats are available in this class.")).toBeInTheDocument();
    expect(applied.sent).toEqual([STUDENT_ID]);
  });

  it("does not ask for a number when one is already saved", async () => {
    const user = userEvent.setup();
    const applied = recorder();
    serveClassList();
    server.use(
      http.post(`${API_BASE}/classes/c_1/holds`, ({ request }) => {
        applied.note(request);
        return apiError(409, "NO_SEAT_AVAILABLE", "No seats are available in this class.");
      }),
    );
    renderWithProviders(undefined, { studentId: STUDENT_ID });

    await user.click(await screen.findByRole("button", { name: APPLY }));

    expect(await screen.findByText("No seats are available in this class.")).toBeInTheDocument();
    expect(screen.queryByText(MESSAGE)).not.toBeInTheDocument();
    expect(applied.sent).toEqual([STUDENT_ID]);
  });
});

describe("confirm and cancel without a registration number (a status link opened on a device that has none)", () => {
  const ROUTE = "/holds/h_1";

  function serveHeldHold() {
    server.use(http.get(`${API_BASE}/holds/h_1`, () => HttpResponse.json(aHold({ status: "HELD" }))));
    serveClass(aClass());
  }

  it("asks for the number instead of confirming, and sends nothing", async () => {
    const user = userEvent.setup();
    const confirmed = recorder();
    serveHeldHold();
    server.use(
      http.post(`${API_BASE}/holds/h_1/confirm`, ({ request }) => {
        confirmed.note(request);
        return refused();
      }),
    );
    renderWithProviders(undefined, { route: ROUTE });
    await screen.findByRole("heading", { name: "Your seat is on hold" });

    await user.click(screen.getByRole("button", { name: "ভর্তি ফি দিন" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(MESSAGE);
    expect(document.body).not.toHaveTextContent("X-Student-Id");
    expect(confirmed.sent).toEqual([]);
    expect(screen.getByLabelText("Registration number")).toHaveFocus();
    // Nothing changed: still held, and the student can fill the number in and try again.
    expect(screen.getByRole("heading", { name: "Your seat is on hold" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "ভর্তি ফি দিন" })).toBeEnabled();
  });

  it("asks for the number instead of cancelling, and sends nothing", async () => {
    const user = userEvent.setup();
    const cancelled = recorder();
    serveHeldHold();
    server.use(
      http.delete(`${API_BASE}/holds/h_1`, ({ request }) => {
        cancelled.note(request);
        return refused();
      }),
    );
    renderWithProviders(undefined, { route: ROUTE });
    await screen.findByRole("heading", { name: "Your seat is on hold" });

    await user.click(screen.getByRole("button", { name: "বাতিল" }));

    expect(await screen.findByRole("alert")).toHaveTextContent(MESSAGE);
    expect(cancelled.sent).toEqual([]);
    expect(screen.getByLabelText("Registration number")).toHaveFocus();
    // It did not leave the screen, as a successful cancel does.
    expect(screen.getByRole("heading", { name: "Your seat is on hold" })).toBeInTheDocument();
  });

  it("confirms with that number once it has been saved", async () => {
    const user = userEvent.setup();
    const confirmed = recorder();
    // A backend that remembers: once the hold is confirmed, reading it says so. (After a
    // confirm the screen asks again, and a fixed "HELD" answer would undo the result.)
    let status: "HELD" | "CONFIRMED" = "HELD";
    server.use(
      http.get(`${API_BASE}/holds/h_1`, () => HttpResponse.json(aHold({ status }))),
      http.post(`${API_BASE}/holds/h_1/confirm`, ({ request }) => {
        confirmed.note(request);
        status = "CONFIRMED";
        return HttpResponse.json({ holdId: "h_1", status: "CONFIRMED" });
      }),
    );
    serveClass(aClass());
    renderWithProviders(undefined, { route: ROUTE });
    await screen.findByRole("heading", { name: "Your seat is on hold" });
    await user.click(screen.getByRole("button", { name: "ভর্তি ফি দিন" }));
    await screen.findByText(MESSAGE);

    await user.type(screen.getByLabelText("Registration number"), STUDENT_ID);
    await user.click(screen.getByRole("button", { name: "Save" }));
    expect(screen.queryByText(MESSAGE)).not.toBeInTheDocument();
    await user.click(screen.getByRole("button", { name: "ভর্তি ফি দিন" }));

    expect(await screen.findByRole("heading", { name: "Admission confirmed" })).toBeInTheDocument();
    expect(confirmed.sent).toEqual([STUDENT_ID]);
  });
});
