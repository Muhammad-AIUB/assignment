import { render, screen } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { expectCrash } from "./test/expectCrash";
import Root from "./Root";

// The in-shell boundary (AppRoutes) protects the screens, but it sits inside the
// header. A crash in the header itself, or in anything above the screens, would still
// unmount everything, so Root puts a last-resort boundary around the whole app.

vi.mock("./components/AppLayout", () => ({
  default: () => {
    throw new Error("the header broke");
  },
}));

let crash: ReturnType<typeof expectCrash>;

beforeEach(() => {
  crash = expectCrash();
});

afterEach(() => {
  crash.restore();
});

describe("Root", () => {
  it("shows a message with a way out when the shell itself crashes, not an empty page", () => {
    render(<Root />);

    const alert = screen.getByRole("alert");
    expect(alert).toHaveTextContent("Something went wrong");
    expect(screen.getByRole("button", { name: "Try again" })).toBeEnabled();
    expect(screen.getByRole("link", { name: /Back to classes/ })).toHaveAttribute("href", "/");
    expect(screen.queryByText(/the header broke/)).not.toBeInTheDocument();
  });
});
