import axios from "axios";
import { getApiBaseUrl } from "../config";

/**
 * The one axios instance every API call goes through.
 *
 * The base URL is resolved from window.__APP_CONFIG__ on each request (see
 * ../config.ts), never from import.meta.env, so the same build can be pointed
 * at a different backend by replacing /config.js.
 */
export const apiClient = axios.create();

apiClient.interceptors.request.use((config) => {
  config.baseURL = getApiBaseUrl();
  return config;
});

/** Header carrying the student's 17-digit registration number. */
export const STUDENT_ID_HEADER = "X-Student-Id";

/** Request config that identifies the student, for the endpoints that need it. */
export function asStudent(studentId: string) {
  return { headers: { [STUDENT_ID_HEADER]: studentId } };
}
