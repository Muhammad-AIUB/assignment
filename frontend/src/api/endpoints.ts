// One typed function per endpoint in docs/API_CONTRACT.md. They return the
// parsed response body and let axios errors propagate: turning 4xx responses
// into user-facing messages is the error-handling session's job.
//
// The student id is an explicit argument; where it is stored (localStorage) is
// not decided here. `signal` lets TanStack Query cancel in-flight requests.

import { apiClient, asStudent } from "./client";
import type {
  ConfirmedHold,
  CreateClassRequest,
  CreatedClass,
  CreatedHold,
  Hold,
  MyHold,
  SchoolClass,
} from "./types";

// Path segments are ids chosen by the server; encode them anyway.
const seg = encodeURIComponent;

export async function createClass(
  body: CreateClassRequest,
  signal?: AbortSignal,
): Promise<CreatedClass> {
  const res = await apiClient.post<CreatedClass>("/classes", body, { signal });
  return res.data;
}

export async function listClasses(signal?: AbortSignal): Promise<SchoolClass[]> {
  const res = await apiClient.get<SchoolClass[]>("/classes", { signal });
  return res.data;
}

export async function getClass(
  classId: string,
  signal?: AbortSignal,
): Promise<SchoolClass> {
  const res = await apiClient.get<SchoolClass>(`/classes/${seg(classId)}`, {
    signal,
  });
  return res.data;
}

export async function createHold(
  classId: string,
  studentId: string,
  signal?: AbortSignal,
): Promise<CreatedHold> {
  const res = await apiClient.post<CreatedHold>(
    `/classes/${seg(classId)}/holds`,
    null,
    { ...asStudent(studentId), signal },
  );
  return res.data;
}

export async function getHold(
  holdId: string,
  signal?: AbortSignal,
): Promise<Hold> {
  const res = await apiClient.get<Hold>(`/holds/${seg(holdId)}`, { signal });
  return res.data;
}

export async function confirmHold(
  holdId: string,
  studentId: string,
  signal?: AbortSignal,
): Promise<ConfirmedHold> {
  const res = await apiClient.post<ConfirmedHold>(
    `/holds/${seg(holdId)}/confirm`,
    null,
    { ...asStudent(studentId), signal },
  );
  return res.data;
}

/** DELETE /holds/{holdId}: cancels a HELD hold. Resolves on 204. */
export async function cancelHold(
  holdId: string,
  studentId: string,
  signal?: AbortSignal,
): Promise<void> {
  await apiClient.delete(`/holds/${seg(holdId)}`, {
    ...asStudent(studentId),
    signal,
  });
}

export async function listMyHolds(
  studentId: string,
  signal?: AbortSignal,
): Promise<MyHold[]> {
  const res = await apiClient.get<MyHold[]>("/me/holds", {
    ...asStudent(studentId),
    signal,
  });
  return res.data;
}
