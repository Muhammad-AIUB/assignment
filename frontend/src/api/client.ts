import axios from "axios";
import { getApiBaseUrl } from "../config";
import { getStudentId } from "../identity/studentIdStore";

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

// Identify the student on every request. The id is read when each request is
// sent, so a change made in the header input applies to the very next call, and
// the endpoint functions never have to be handed an id. Before an id has been
// entered no header is sent (not an empty one); the endpoints that need it then
// answer 400, which the error-handling session turns into a message.
apiClient.interceptors.request.use((config) => {
  const studentId = getStudentId();
  if (studentId !== null) {
    config.headers.set(STUDENT_ID_HEADER, studentId);
  }
  return config;
});
