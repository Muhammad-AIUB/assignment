// The create-class form's rules, as one pure function (no React, no DOM) so they
// can be checked on their own. They mirror the backend's
// CreateClassRequestValidator and its `int TotalSeats`, so a request this lets
// through is one the backend is expected to accept. The backend stays the
// authority: anything it still refuses comes back as a 400 and is shown inline.

import type { CreateClassRequest } from "../api/types";

/** Backend: CreateClassRequestValidator.NameMaxLength. */
export const CLASS_NAME_MAX_LENGTH = 200;

/** Backend: TotalSeats is a C# `int`, so a larger number cannot be bound. */
export const TOTAL_SEATS_MAX = 2_147_483_647;

export interface ClassFormErrors {
  name?: string;
  totalSeats?: string;
}

export type ClassFormResult =
  | { ok: true; request: CreateClassRequest }
  | { ok: false; errors: ClassFormErrors };

/**
 * Checks what was typed into the form. `totalSeats` is the raw text of the
 * field. `seatsUnreadable` is true when the browser has text in a number
 * field that it cannot turn into a number (it then reports an empty value, and
 * without this the message would wrongly say the field is empty).
 *
 * On success the request carries the trimmed name (the backend trims too) and
 * the seat count as a number.
 */
export function validateClassForm(input: {
  name: string;
  totalSeats: string;
  seatsUnreadable?: boolean;
}): ClassFormResult {
  const errors: ClassFormErrors = {};

  const name = input.name.trim();
  if (name === "") {
    errors.name = "Enter a class name.";
  } else if (name.length > CLASS_NAME_MAX_LENGTH) {
    errors.name = `Class name must be ${CLASS_NAME_MAX_LENGTH} characters or fewer (it is ${name.length}).`;
  }

  const seatsText = input.totalSeats.trim();
  if (seatsText === "" && !input.seatsUnreadable) {
    errors.totalSeats = "Enter the total number of seats.";
  } else if (input.seatsUnreadable || !/^[0-9]+$/.test(seatsText)) {
    // Letters, a minus sign, a decimal point, an exponent: not a count of seats.
    errors.totalSeats = "Total seats must be a whole number greater than 0, for example 120.";
  } else {
    const seats = Number(seatsText);
    if (seats <= 0) {
      errors.totalSeats = "Total seats must be greater than 0.";
    } else if (seats > TOTAL_SEATS_MAX) {
      errors.totalSeats = `Total seats must be ${TOTAL_SEATS_MAX.toLocaleString("en-US")} or fewer.`;
    }
  }

  if (errors.name !== undefined || errors.totalSeats !== undefined) {
    return { ok: false, errors };
  }
  return { ok: true, request: { name, totalSeats: Number(seatsText) } };
}
