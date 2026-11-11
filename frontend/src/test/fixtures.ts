import { http, HttpResponse } from "msw";
import type { Hold, SchoolClass } from "../api/types";
import { API_BASE, server } from "./server";

/** A valid 17-digit registration number. */
export const STUDENT_ID = "20152691234567890";

/** A hold as GET /holds/:id returns it. Expires in ten minutes unless told otherwise. */
export function aHold(overrides: Partial<Hold> = {}): Hold {
  return {
    holdId: "h_1",
    classId: "c_1",
    status: "HELD",
    expiresAt: new Date(Date.now() + 10 * 60_000).toISOString(),
    ...overrides,
  };
}

/** A class as GET /classes/:id returns it. */
export function aClass(overrides: Partial<SchoolClass> = {}): SchoolClass {
  return {
    id: "c_1",
    name: "Class 6 - Morning Shift",
    totalSeats: 120,
    heldSeats: 15,
    confirmedSeats: 80,
    availableSeats: 25,
    ...overrides,
  };
}

/** The body of every 4xx the backend sends: { error, message }. */
export function apiError(status: number, error: string, message: string) {
  return HttpResponse.json({ error, message }, { status });
}

/** GET /classes/:id for the class a status screen shows the name of. */
export function serveClass(schoolClass: SchoolClass = aClass()) {
  server.use(
    http.get(`${API_BASE}/classes/${schoolClass.id}`, () => HttpResponse.json(schoolClass)),
  );
}
