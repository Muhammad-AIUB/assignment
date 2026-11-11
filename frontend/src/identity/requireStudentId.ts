import { useCallback, useState } from "react";
import { useStudentId } from "./StudentIdProvider";

/**
 * What a screen says when the student acts (apply, confirm, cancel) before entering a
 * registration number. The backend's own answer to a request with no number is
 * "X-Student-Id header is required and must be exactly 17 digits.", which names an HTTP
 * header and does not say what to do, so the request is not sent at all.
 */
export const MISSING_STUDENT_ID_MESSAGE =
  "Enter your 17-digit registration number at the top of the page, then try again.";

/** The registration number field in the header (see components/StudentIdInput). */
const STUDENT_ID_INPUT_SELECTOR = 'input[name="studentId"]';

/** Puts the cursor in the registration number field, which also scrolls it into view. */
export function focusStudentIdInput(): void {
  document.querySelector<HTMLInputElement>(STUDENT_ID_INPUT_SELECTOR)?.focus();
}

/**
 * Guard for an action that needs a registration number.
 *
 * Call `ensure()` first: with a number saved it returns true and the action goes ahead.
 * Without one it returns false, moves the cursor to the registration field and turns
 * `missing` on, so the screen can say what to do. `missing` turns itself off as soon as
 * a number is saved, so the message never outlives its reason and no effect is needed.
 */
export function useRequireStudentId(): { missing: boolean; ensure: () => boolean } {
  const { studentId } = useStudentId();
  const [asked, setAsked] = useState(false);

  const ensure = useCallback(() => {
    if (studentId !== null) {
      setAsked(false);
      return true;
    }
    setAsked(true);
    focusStudentIdInput();
    return false;
  }, [studentId]);

  return { missing: asked && studentId === null, ensure };
}
