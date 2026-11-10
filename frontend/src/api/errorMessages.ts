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
 * backend sent no message of its own (server unreachable, an unexpected
 * failure). Null when there is nothing more to add than GENERIC_ERROR.
 */
export function fallbackDetail(error: unknown): string | null {
  if (axios.isAxiosError(error) && !error.response) {
    return "Could not reach the server.";
  }
  if (error instanceof Error && error.message.trim() !== "") {
    return error.message;
  }
  return null;
}
