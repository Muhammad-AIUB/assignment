import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { expectCrash } from "../test/expectCrash";
import ErrorBoundary from "./ErrorBoundary";

// docs/FRONTEND_SPEC.md: "Wrap the app in a React error boundary so a render crash
// never produces a fully blank page."

/** A child that throws while it renders for as long as `explode.current` is true. */
function Bomb({ explode }: { explode: { current: boolean } }) {
  if (explode.current) throw new Error("kaboom: classes.map is not a function");
  return <p>All fine</p>;
}

let crash: ReturnType<typeof expectCrash>;

beforeEach(() => {
  // These tests make a child throw on purpose: keep the output readable, and look at
  // what the boundary logs itself.
  crash = expectCrash();
});

afterEach(() => {
  crash.restore();
});

describe("ErrorBoundary", () => {
  it("renders its children when nothing goes wrong", () => {
    render(
      <ErrorBoundary>
        <Bomb explode={{ current: false }} />
      </ErrorBoundary>,
    );

    expect(screen.getByText("All fine")).toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("shows a friendly message instead of a blank page when a child throws while rendering", () => {
    render(
      <>
        <p>The header stays</p>
        <ErrorBoundary>
          <Bomb explode={{ current: true }} />
        </ErrorBoundary>
      </>,
    );

    const alert = screen.getByRole("alert");
    expect(alert).toHaveTextContent("Something went wrong");
    expect(alert).toHaveTextContent(
      "This screen stopped working. Try again, or go back to the classes.",
    );
    expect(screen.getByRole("button", { name: "Try again" })).toBeEnabled();
    expect(screen.getByRole("link", { name: /Back to classes/ })).toHaveAttribute("href", "/");
    // What is outside the boundary is untouched.
    expect(screen.getByText("The header stays")).toBeInTheDocument();
  });

  it("does not show the technical error to the student, but logs it", () => {
    render(
      <ErrorBoundary>
        <Bomb explode={{ current: true }} />
      </ErrorBoundary>,
    );

    expect(screen.queryByText(/kaboom/)).not.toBeInTheDocument();
    expect(screen.queryByText(/classes\.map/)).not.toBeInTheDocument();
    // In a production build React stays silent about errors a boundary catches, so the
    // boundary has to leave the trace itself.
    const logged = crash.consoleError.mock.calls.some(
      (args) =>
        String(args[0]).includes("[ErrorBoundary]") &&
        args.some((a) => a instanceof Error && a.message.startsWith("kaboom")),
    );
    expect(logged).toBe(true);
  });

  it("renders the children again on Try again, and tells the owner through onReset", async () => {
    const user = userEvent.setup();
    const explode = { current: true };
    const onReset = vi.fn();
    render(
      <ErrorBoundary onReset={onReset}>
        <Bomb explode={explode} />
      </ErrorBoundary>,
    );
    expect(screen.getByRole("alert")).toBeInTheDocument();

    explode.current = false; // the cause is gone
    await user.click(screen.getByRole("button", { name: "Try again" }));

    expect(screen.getByText("All fine")).toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
    expect(onReset).toHaveBeenCalledTimes(1);
  });

  it("shows the message again, never a blank page, when Try again fails again", async () => {
    const user = userEvent.setup();
    render(
      <ErrorBoundary>
        <Bomb explode={{ current: true }} />
      </ErrorBoundary>,
    );

    await user.click(screen.getByRole("button", { name: "Try again" }));

    expect(screen.getByRole("alert")).toHaveTextContent("Something went wrong");
    expect(screen.getByRole("button", { name: "Try again" })).toBeEnabled();
  });

  it("clears the message by itself when resetKey changes, as it does when the student goes to another page", () => {
    const explode = { current: true };
    const { rerender } = render(
      <ErrorBoundary resetKey="/classes">
        <Bomb explode={explode} />
      </ErrorBoundary>,
    );
    expect(screen.getByRole("alert")).toBeInTheDocument();

    explode.current = false;
    rerender(
      <ErrorBoundary resetKey="/me">
        <Bomb explode={explode} />
      </ErrorBoundary>,
    );

    expect(screen.getByText("All fine")).toBeInTheDocument();
    expect(screen.queryByRole("alert")).not.toBeInTheDocument();
  });

  it("keeps the message while resetKey stays the same", () => {
    const explode = { current: true };
    const { rerender } = render(
      <ErrorBoundary resetKey="/classes">
        <Bomb explode={explode} />
      </ErrorBoundary>,
    );

    explode.current = false;
    rerender(
      <ErrorBoundary resetKey="/classes">
        <Bomb explode={explode} />
      </ErrorBoundary>,
    );

    expect(screen.getByRole("alert")).toBeInTheDocument();
  });
});
