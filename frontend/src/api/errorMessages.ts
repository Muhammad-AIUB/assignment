import axios from "axios";
import type { ApiErrorBody } from "./types";

/** Fallback wording from docs/FRONTEND_SPEC.md ("Error handling"). */
export const GENERIC_ERROR = "কিছু একটা ভুল হয়েছে, আবার চেষ্টা করুন";

/**
 * The `message` of the backend's `{ error, message }` body, if the error has
 * one. This is the backend's own wording, so it is shown as it is.
 */
export function backendMessage(error: unknown): string | null {
  if (axios.isAxiosError<ApiErrorBody>(error)) {
    const message = error.response?.data?.message;
    if (typeof message === "string" && message.trim() !== "") {
      return message;
    }
  }
  return null;
}

/**
 * One plain-language line saying what went wrong, for the cases where the
 * backend sent no message of its own (server unreachable, a 5xx or a 4xx with
 * a body that is not `{ error, message }`, an unexpected failure). Null when
 * there is nothing more to add than GENERIC_ERROR.
 */
export function fallbackDetail(error: unknown): string | null {
  if (axios.isAxiosError(error)) {
    if (!error.response) {
      return "Could not reach the server.";
    }
    const { status } = error.response;
    return status >= 500
      ? `The server had a problem (HTTP ${status}).`
      : `The server refused the request (HTTP ${status}).`;
  }
  if (error instanceof Error && error.message.trim() !== "") {
    return error.message;
  }
  return null;
}

/** What a screen shows for a failed request; see describeApiError. */
export interface ApiErrorDescription {
  /** The backend's own message, or the generic Bangla line when it sent none. */
  message: string;
  /** A short second line, only when the backend sent no message of its own. */
  detail: string | null;
  /** True when `message` is the backend's wording (shown as written, English). */
  fromBackend: boolean;
}

/**
 * The one place that decides what a failed request says, for every screen.
 *
 * The backend's own `message` is shown as written (no code-to-text table: the
 * backend's wording is the source of truth). When there is none (the server is
 * down, a 5xx, a body that is not JSON) it is the generic line from
 * docs/FRONTEND_SPEC.md plus a short detail saying what kind of failure it was.
 */
export function describeApiError(error: unknown): ApiErrorDescription {
  const message = backendMessage(error);
  if (message !== null) {
    return { message, detail: null, fromBackend: true };
  }
  return { message: GENERIC_ERROR, detail: fallbackDetail(error), fromBackend: false };
}
