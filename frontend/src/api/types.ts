// Request/response shapes of the backend, copied from docs/API_CONTRACT.md.
// That contract is frozen: field names here must match it exactly.

export type HoldStatus = "HELD" | "CONFIRMED" | "EXPIRED" | "CANCELLED";

/** Body of every 4xx response: `{ "error": "SOME_CODE", "message": "..." }`. */
export interface ApiErrorBody {
  error: string;
  message: string;
}

/** POST /classes request. */
export interface CreateClassRequest {
  name: string;
  totalSeats: number;
}

/** POST /classes response (201). */
export interface CreatedClass {
  id: string;
  name: string;
  totalSeats: number;
}

/** GET /classes (list item) and GET /classes/{classId}. */
export interface SchoolClass {
  id: string;
  name: string;
  totalSeats: number;
  heldSeats: number;
  confirmedSeats: number;
  availableSeats: number;
}

/** POST /classes/{classId}/holds response (201). */
export interface CreatedHold {
  holdId: string;
  classId: string;
  studentId: string;
  status: HoldStatus;
  /** ISO 8601 UTC instant, e.g. "2026-11-01T04:10:00Z". */
  expiresAt: string;
}

/** GET /holds/{holdId}. */
export interface Hold {
  holdId: string;
  classId: string;
  status: HoldStatus;
  expiresAt: string;
}

/** POST /holds/{holdId}/confirm response (200). */
export interface ConfirmedHold {
  holdId: string;
  status: HoldStatus;
}

/** GET /me/holds (list item). */
export interface MyHold {
  holdId: string;
  classId: string;
  className: string;
  status: HoldStatus;
  expiresAt: string;
}
