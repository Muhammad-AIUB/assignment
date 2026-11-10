// Presentation helpers for a class's seat numbers. Pure functions of the numbers
// GET /classes returns (docs/API_CONTRACT.md); nothing here talks to the API.

import type { SchoolClass } from "../api/types";

/** How many segments the seat strip on a class card is drawn with. */
export const SEAT_SEGMENTS = 30;

/** At or below this share of seats still available, a class is "filling fast". */
export const FILLING_FAST_RATIO = 0.2;

export type SeatStatus = "open" | "filling" | "full";

export interface SeatSegments {
  confirmed: number;
  held: number;
  free: number;
}

/**
 * How many of `count` strip segments are confirmed, on hold and free.
 *
 * The strip is a rounded picture of the real numbers, and it must never say
 * something the numbers do not: the three parts always add up to `count`, a
 * class with even one seat left always shows at least one free segment, a class
 * with no seat left shows none, and any confirmed or held seat shows at least
 * one segment of its own.
 */
export function seatSegments(
  total: number,
  held: number,
  confirmed: number,
  count: number = SEAT_SEGMENTS,
): SeatSegments {
  const heldSeats = Math.max(0, held);
  const confirmedSeats = Math.max(0, confirmed);
  if (total <= 0) return { confirmed: 0, held: 0, free: count };

  const seatsLeft = Math.max(0, total - heldSeats - confirmedSeats);
  const scale = (seats: number) => Math.round((seats / total) * count);

  let confirmedSegments = Math.min(count, scale(confirmedSeats));
  let heldSegments = Math.min(count, scale(heldSeats));
  // A seat that exists is never rounded away to nothing.
  if (confirmedSeats > 0 && confirmedSegments === 0) confirmedSegments = 1;
  if (heldSeats > 0 && heldSegments === 0) heldSegments = 1;

  if (seatsLeft > 0) {
    // Leave room for the free segment. Shrink whichever side is bigger, but
    // never below the one segment a non-empty side is owed.
    while (confirmedSegments + heldSegments > count - 1) {
      if (confirmedSegments >= heldSegments && confirmedSegments > 1) confirmedSegments -= 1;
      else if (heldSegments > 1) heldSegments -= 1;
      else confirmedSegments = Math.max(0, confirmedSegments - 1);
    }
  } else if (heldSeats === 0) {
    confirmedSegments = count; // sold out and nothing on hold: the strip is solid
    heldSegments = 0;
  } else if (confirmedSeats === 0) {
    heldSegments = count; // everything is on hold
    confirmedSegments = 0;
  } else {
    // Sold out: no free segment. Keep the held share and give the rest to confirmed.
    heldSegments = Math.min(heldSegments, count - 1);
    confirmedSegments = count - heldSegments;
  }

  return {
    confirmed: confirmedSegments,
    held: heldSegments,
    free: count - confirmedSegments - heldSegments,
  };
}

/** "full" with no seat left, "filling" when little is left, otherwise "open". */
export function seatStatus(schoolClass: SchoolClass): SeatStatus {
  if (schoolClass.availableSeats <= 0) return "full";
  if (schoolClass.totalSeats > 0 && schoolClass.availableSeats / schoolClass.totalSeats <= FILLING_FAST_RATIO) {
    return "filling";
  }
  return "open";
}

/**
 * "Class 6 - Morning Shift" becomes a title ("Class 6") and a second line
 * ("Morning Shift"), which lets a card give the class level its own weight. A
 * name with no " - " in it is all title. Only the first " - " splits, so a name
 * like "Class 6 - Morning - Girls" keeps "Morning - Girls" together.
 */
export function splitClassName(name: string): { title: string; subtitle: string | null } {
  const at = name.indexOf(" - ");
  if (at <= 0) return { title: name, subtitle: null };
  const title = name.slice(0, at).trim();
  const subtitle = name.slice(at + 3).trim();
  return title === "" || subtitle === "" ? { title: name, subtitle: null } : { title, subtitle };
}
