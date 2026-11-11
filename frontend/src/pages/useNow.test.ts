import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { useNow } from "./useNow";

const START = Date.UTC(2026, 10, 11, 10, 0, 0);

describe("useNow", () => {
  beforeEach(() => {
    vi.useFakeTimers();
    vi.setSystemTime(START);
  });
  afterEach(() => {
    vi.useRealTimers();
  });

  it("starts at the current time", () => {
    const { result } = renderHook(() => useNow(START + 60_000));

    expect(result.current).toBe(START);
  });

  it("renews the time every second while there is a deadline", () => {
    const { result } = renderHook(() => useNow(START + 60_000));

    act(() => {
      vi.advanceTimersByTime(3_000);
    });

    expect(result.current).toBe(START + 3_000);
  });

  it("runs no timer at all when there is nothing to count down to", () => {
    const { result } = renderHook(() => useNow(null));

    act(() => {
      vi.advanceTimersByTime(5_000);
    });

    expect(vi.getTimerCount()).toBe(0);
    expect(result.current).toBe(START);
  });

  it("updates just after the deadline itself, not up to a second late", () => {
    const deadline = START + 2_500; // between the 2 s and 3 s ticks
    const { result } = renderHook(() => useNow(deadline));

    act(() => {
      vi.advanceTimersByTime(2_400);
    });
    expect(result.current).toBeLessThan(deadline);

    act(() => {
      vi.advanceTimersByTime(200);
    });
    expect(result.current).toBeGreaterThanOrEqual(deadline);
  });

  it("renews the time as soon as a hidden tab becomes visible again", () => {
    vi.spyOn(document, "visibilityState", "get").mockReturnValue("visible");
    const { result } = renderHook(() => useNow(START + 600_000));

    // A background tab's timers are throttled: the clock moved, the timers did not run.
    vi.setSystemTime(START + 45_000);
    act(() => {
      document.dispatchEvent(new Event("visibilitychange"));
    });

    expect(result.current).toBe(START + 45_000);
  });

  it("stops its timers when the screen goes away", () => {
    const { unmount } = renderHook(() => useNow(START + 60_000));

    unmount();

    expect(vi.getTimerCount()).toBe(0);
  });
});
