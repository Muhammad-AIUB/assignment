import { useId, useState, type FormEvent } from "react";
import { useStudentId } from "../identity/StudentIdProvider";

const INVALID_MESSAGE = "Enter exactly 17 digits (0-9), for example 20152691234567890.";

/**
 * The one place a student tells the app who they are: their 17-digit
 * registration number. Nothing is accepted (or stored, or sent to the API)
 * until it is exactly 17 digits; otherwise an inline error is shown.
 */
export default function StudentIdInput() {
  const { studentId, setStudentId } = useStudentId();
  const [draft, setDraft] = useState(studentId ?? "");
  const [error, setError] = useState<string | null>(null);
  const inputId = useId();
  const messageId = useId();

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    // Surrounding whitespace is a copy-paste artefact, not part of the number.
    const value = draft.trim();
    if (setStudentId(value)) {
      setDraft(value);
      setError(null);
    } else {
      setError(INVALID_MESSAGE);
    }
  }

  const saved = studentId !== null && draft.trim() === studentId;

  return (
    <form className="student-id-form" onSubmit={handleSubmit} noValidate>
      <label htmlFor={inputId}>Registration number</label>
      <input
        id={inputId}
        name="studentId"
        type="text"
        inputMode="numeric"
        autoComplete="off"
        value={draft}
        onChange={(event) => {
          setDraft(event.target.value);
          setError(null);
        }}
        aria-invalid={error !== null}
        aria-describedby={messageId}
      />
      <button type="submit">Save</button>
      <p
        id={messageId}
        className={error !== null ? "student-id-message error" : "student-id-message"}
        role={error !== null ? "alert" : undefined}
      >
        {error ??
          (saved
            ? "Registration number set."
            : studentId === null
              ? "Needed to apply for a seat and to see your applications."
              : "Press Save to switch to this number.")}
      </p>
    </form>
  );
}
