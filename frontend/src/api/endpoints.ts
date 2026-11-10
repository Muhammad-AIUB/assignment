// One typed function per endpoint in docs/API_CONTRACT.md. They return the
// parsed response body and let axios errors propagate: turning 4xx responses
// into user-facing messages is the error-handling session's job.
//
// The student id is not an argument: the request interceptor in ./client.ts
// adds the X-Student-Id header to every call from the id the student entered in
// the header input (see ../identity). `signal` lets TanStack Query cancel
// in-flight requests.

import { apiClient } from "./client";
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
  signal?: AbortSignal,
): Promise<CreatedHold> {
  const res = await apiClient.post<CreatedHold>(
    `/classes/${seg(classId)}/holds`,
    null,
    { signal },
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
  signal?: AbortSignal,
): Promise<ConfirmedHold> {
  const res = await apiClient.post<ConfirmedHold>(
    `/holds/${seg(holdId)}/confirm`,
    null,
    { signal },
  );
  return res.data;
}

/** DELETE /holds/{holdId}: cancels a HELD hold. Resolves on 204. */
export async function cancelHold(
  holdId: string,
  signal?: AbortSignal,
): Promise<void> {
  await apiClient.delete(`/holds/${seg(holdId)}`, { signal });
}

export async function listMyHolds(signal?: AbortSignal): Promise<MyHold[]> {
  const res = await apiClient.get<MyHold[]>("/me/holds", { signal });
  return res.data;
}
