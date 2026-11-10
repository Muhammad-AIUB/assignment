import {
  createContext,
  useContext,
  useMemo,
  useSyncExternalStore,
  type ReactNode,
} from "react";
import { getStudentId, setStudentId, subscribeStudentId } from "./studentIdStore";

export interface StudentIdContextValue {
  /** The current student id (17 digits), or null until one has been entered. */
  studentId: string | null;
  /**
   * Accepts and persists a new id. Returns false, changing nothing, unless the
   * value is exactly 17 digits.
   */
  setStudentId: (value: string) => boolean;
}

const StudentIdContext = createContext<StudentIdContextValue | null>(null);

/**
 * Holds the current student id for the component tree. The value is loaded from
 * localStorage when the app starts and every accepted change is written back to
 * it; the same value is what the axios interceptor sends as X-Student-Id.
 */
export function StudentIdProvider({ children }: { children: ReactNode }) {
  const studentId = useSyncExternalStore(subscribeStudentId, getStudentId);
  const value = useMemo(() => ({ studentId, setStudentId }), [studentId]);
  return (
    <StudentIdContext.Provider value={value}>
      {children}
    </StudentIdContext.Provider>
  );
}

export function useStudentId(): StudentIdContextValue {
  const value = useContext(StudentIdContext);
  if (value === null) {
    throw new Error("useStudentId must be used inside <StudentIdProvider>.");
  }
  return value;
}
