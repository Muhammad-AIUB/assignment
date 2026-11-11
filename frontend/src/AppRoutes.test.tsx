import { QueryClient } from "@tanstack/react-query";
import { screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { http, HttpResponse } from "msw";
import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { aClass } from "./test/fixtures";
import { expectCrash } from "./test/expectCrash";
import { renderWithProviders } from "./test/renderWithProviders";
import { API_BASE, server } from "./test/server";

// docs/FRONTEND_SPEC.md: no error may leave a blank screen, and "a render crash never
// produces a fully blank page". These run the real shell and route table.

describe("an address that matches no screen", () => {
  it.each(["/nope/not-a-page", "/holds", "/admin"])(
    "%s shows a page-not-found message inside the shell, not an empty page",
    async (route) => {
      renderWithProviders(undefined, { route });

      expect(await screen.findByRole("heading", { name: "Page not found" })).toBeInTheDocument();
      expect(screen.getByRole("link", { name: /Browse classes/ })).toHaveAttribute("href", "/");
      // The shell around it is still there.
      expect(screen.getByLabelText("Registration number")).toBeInTheDocument();
      expect(screen.getByRole("navigation", { name: "Main" })).toBeInTheDocument();
    },
  );
});

describe("a screen that crashes while it renders", () => {
  let crash: ReturnType<typeof expectCrash>;

  beforeEach(() => {
    crash = expectCrash();
  });

  afterEach(() => {
    crash.restore();
  });

  /**
   * GET /classes answers with something that is not a list `badAnswers` times, then
   * properly: a backend that broke its contract for a moment. The class list cannot
   * draw an object, so the screen throws while it renders.
   */
  function serveClassesBadThenGood(badAnswers: number) {
    let calls = 0;
    server.use(
      http.get(`${API_BASE}/classes`, () => {
        calls += 1;
        return calls <= badAnswers
          ? HttpResponse.json({ surprise: "the server sent something that is not a list" })
          : HttpResponse.json([aClass({ name: "Class 6 - Morning Shift" })]);
      }),
    );
  }

  /** A client that keeps its cache like the real app (the test default drops it at once). */
  const appLikeClient = () => new QueryClient({ defaultOptions: { queries: { retry: false } } });

  it("shows a message and keeps the header, instead of an empty page", async () => {
    serveClassesBadThenGood(1);
    renderWithProviders(undefined, { queryClient: appLikeClient() });

    const alert = await screen.findByRole("alert");
    expect(alert).toHaveTextContent("Something went wrong");
    // The header, the navigation and the registration number input are still on screen.
    expect(screen.getByLabelText("Registration number")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "My applications" })).toBeInTheDocument();
  });

  it("recovers on Try again once the backend answers properly, and does not reuse the bad data it kept", async () => {
    const user = userEvent.setup();
    serveClassesBadThenGood(1);
    renderWithProviders(undefined, { queryClient: appLikeClient() });
    await screen.findByRole("alert");

    await user.click(screen.getByRole("button", { name: "Try again" }));

    expect(await screen.findByRole("heading", { name: /Class 6.*Morning Shift/ })).toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("shows the message again, not an empty page, when the answer is still wrong", async () => {
    const user = userEvent.setup();
    serveClassesBadThenGood(Number.MAX_SAFE_INTEGER);
    renderWithProviders(undefined, { queryClient: appLikeClient() });
    await screen.findByRole("alert");

    await user.click(screen.getByRole("button", { name: "Try again" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("Something went wrong");
    expect(screen.getByLabelText("Registration number")).toBeInTheDocument();
  });

  it("clears the message when the student goes to another page", async () => {
    const user = userEvent.setup();
    serveClassesBadThenGood(Number.MAX_SAFE_INTEGER);
    renderWithProviders(undefined, { queryClient: appLikeClient() });
    const alert = await screen.findByRole("alert");

    await user.click(within(screen.getByRole("navigation", { name: "Main" })).getByRole("link", { name: "My applications" }));

    expect(screen.queryByText("Something went wrong")).not.toBeInTheDocument();
    expect(screen.getByRole("heading", { name: "My applications" })).toBeInTheDocument();
    expect(alert).not.toBeInTheDocument();
  });
});
