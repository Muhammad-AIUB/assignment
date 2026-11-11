import { AxiosError, type AxiosResponse } from "axios";
import { describe, expect, it } from "vitest";
import { describeApiError, GENERIC_ERROR } from "./errorMessages";

/** An axios error as the app sees it: with a response when the server answered. */
function httpError(status: number, data: unknown): AxiosError {
  const response = { status, data, statusText: "", headers: {}, config: {} } as AxiosResponse;
  return new AxiosError(`Request failed with status code ${status}`, "ERR_BAD_REQUEST", undefined, null, response);
}

/** What axios throws when no response arrived at all (server down, CORS, offline). */
function networkError(): AxiosError {
  return new AxiosError("Network Error", "ERR_NETWORK");
}

describe("describeApiError", () => {
  it("shows the backend's own message, as written, with nothing added", () => {
    const error = httpError(409, { error: "HOLD_EXPIRED", message: "This hold has expired." });

    expect(describeApiError(error)).toEqual({
      message: "This hold has expired.",
      detail: null,
      fromBackend: true,
    });
  });

  it("falls back to the generic fallback line when the backend's message is blank", () => {
    const error = httpError(400, { error: "VALIDATION_ERROR", message: "   " });

    expect(describeApiError(error).message).toBe(GENERIC_ERROR);
    expect(describeApiError(error).fromBackend).toBe(false);
  });

  it("says the server could not be reached when no response arrived", () => {
    expect(describeApiError(networkError())).toEqual({
      message: GENERIC_ERROR,
      detail: "Could not reach the server.",
      fromBackend: false,
    });
  });

  it("says the server had a problem for a 5xx with a body that is not JSON", () => {
    const error = httpError(502, "<html>Bad Gateway</html>");

    expect(describeApiError(error)).toEqual({
      message: GENERIC_ERROR,
      detail: "The server had a problem (HTTP 502).",
      fromBackend: false,
    });
  });

  it("says the request was refused for a 4xx that carries no message", () => {
    const error = httpError(404, null);

    expect(describeApiError(error)).toEqual({
      message: GENERIC_ERROR,
      detail: "The server refused the request (HTTP 404).",
      fromBackend: false,
    });
  });

  it("uses the text of an ordinary Error as the detail line", () => {
    const error = new Error("window.__APP_CONFIG__.apiBaseUrl is not set.");

    expect(describeApiError(error)).toEqual({
      message: GENERIC_ERROR,
      detail: "window.__APP_CONFIG__.apiBaseUrl is not set.",
      fromBackend: false,
    });
  });

  it("still gives the generic line, with no detail, for something that is not an error at all", () => {
    expect(describeApiError(null)).toEqual({ message: GENERIC_ERROR, detail: null, fromBackend: false });
    expect(describeApiError("boom")).toEqual({ message: GENERIC_ERROR, detail: null, fromBackend: false });
  });
});
