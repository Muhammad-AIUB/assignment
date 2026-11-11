import { describe, expect, it } from "vitest";
import { formatCountdown, parseInstant, resolveHold } from "./holdCountdown";

// Ported from the 31 checks Session 17 ran outside the repo, now kept as tests.

describe("parseInstant", () => {
  const expected = Date.UTC(2026, 10, 11, 10, 0, 0, 123);

  it("reads the 7 fractional digits the backend writes when a hold is created", () => {
    expect(parseInstant("2026-11-11T10:00:00.1234567Z")).toBe(expected);
  });

  it("reads the 6 fractional digits the backend writes when a hold is read", () => {
    expect(parseInstant("2026-11-11T10:00:00.123456Z")).toBe(expected);
  });

  it("reads an instant with no fraction, and one with an offset", () => {
    expect(parseInstant("2026-11-11T10:00:00Z")).toBe(Date.UTC(2026, 10, 11, 10, 0, 0));
    expect(parseInstant("2026-11-11T16:00:00+06:00")).toBe(Date.UTC(2026, 10, 11, 10, 0, 0));
  });

  it("gives NaN for text that is not a date", () => {
    expect(parseInstant("soon")).toBeNaN();
  });
});

describe("formatCountdown", () => {
  it.each([
    [0, "00:00"],
    [-5_000, "00:00"],
    [1, "00:01"],
    [1_000, "00:01"],
    [1_001, "00:02"],
    [59_000, "00:59"],
    [60_000, "01:00"],
    [600_000, "10:00"],
    [3_599_001, "1:00:00"],
    [3_600_000, "1:00:00"],
    [3_661_000, "1:01:01"],
  ])("shows %i ms left as %s", (remainingMs, text) => {
    expect(formatCountdown(remainingMs)).toBe(text);
  });
});

describe("resolveHold", () => {
  const deadline = Date.UTC(2026, 10, 11, 10, 10, 0);

  it("shows HELD with the time left while the deadline is in the future", () => {
    const view = resolveHold({
      serverStatus: "HELD",
      expiresAtMs: deadline,
      nowMs: deadline - 30_000,
      fetchedAtMs: deadline - 31_000,
    });

    expect(view).toMatchObject({ status: "HELD", remainingMs: 30_000, countdownDone: false });
  });

  it("shows EXPIRED at once when the countdown reaches zero and the last answer is older than the deadline", () => {
    const view = resolveHold({
      serverStatus: "HELD",
      expiresAtMs: deadline,
      nowMs: deadline,
      fetchedAtMs: deadline - 2_000,
    });

    expect(view).toMatchObject({ status: "EXPIRED", countdownDone: true, expiredLocally: true });
  });

  it("trusts a server answer from after the deadline that still says HELD (this device's clock is ahead)", () => {
    const view = resolveHold({
      serverStatus: "HELD",
      expiresAtMs: deadline,
      nowMs: deadline + 5_000,
      fetchedAtMs: deadline + 1_000,
    });

    expect(view).toMatchObject({ status: "HELD", serverDisagrees: true, expiredLocally: false });
  });

  it.each(["CONFIRMED", "CANCELLED", "EXPIRED"] as const)(
    "shows %s as the server says it, even after the deadline",
    (serverStatus) => {
      const view = resolveHold({
        serverStatus,
        expiresAtMs: deadline,
        nowMs: deadline + 60_000,
        fetchedAtMs: deadline - 1_000,
      });

      expect(view.status).toBe(serverStatus);
      expect(view.expiredLocally).toBe(false);
    },
  );

  it("has no countdown when the deadline cannot be read, and keeps the server's status", () => {
    const view = resolveHold({
      serverStatus: "HELD",
      expiresAtMs: Number.NaN,
      nowMs: deadline,
      fetchedAtMs: deadline,
    });

    expect(view).toMatchObject({ status: "HELD", remainingMs: null, countdownDone: false });
  });
});
