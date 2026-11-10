import { useId, useState, type FormEvent } from "react";
import { useStudentId } from "../identity/StudentIdProvider";
import { CheckIcon, IdCardIcon } from "./icons";

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
  // Drives the colours in AppLayout.css; the text below is what tells people.
  const state = error !== null ? "error" : saved ? "saved" : "idle";

  return (
    <form
      className="student-id-form"
      data-state={state}
      onSubmit={handleSubmit}
      noValidate
    >
      <label className="student-id-label" htmlFor={inputId}>
        Registration number
      </label>
      <div className="student-id-field">
        <span className="student-id-icon">
          {saved ? <CheckIcon /> : <IdCardIcon />}
        </span>
        <input
          id={inputId}
          name="studentId"
          type="text"
          inputMode="numeric"
          autoComplete="off"
          placeholder="17-digit number"
          value={draft}
          onChange={(event) => {
            setDraft(event.target.value);
            setError(null);
          }}
          aria-invalid={error !== null}
          aria-describedby={messageId}
        />
        <button type="submit" className="student-id-save">
          Save
        </button>
      </div>
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
