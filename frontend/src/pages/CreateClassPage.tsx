import { useEffect, useRef, useState, type FormEvent } from "react";
import { Link, useNavigate } from "react-router-dom";
import { backendMessage, fallbackDetail, GENERIC_ERROR } from "../api/errorMessages";
import { useCreateClass } from "../api/useCreateClass";
import { AlertIcon, ArrowRightIcon, CheckIcon } from "../components/icons";
import { validateClassForm, type ClassFormErrors } from "./createClassForm";
import "./CreateClassPage.css";

/** How long the success message stays up before the redirect to the class list. */
const REDIRECT_DELAY_MS = 1800;

/**
 * Why the request failed. A 400 from the backend carries its own `message`
 * (for example which field it refused), and that is shown as it is. With no
 * backend message (server unreachable, an unexpected failure) it falls back to
 * the spec's generic line plus whatever detail there is.
 */
function CreateErrorText({ error }: { error: unknown }) {
  const message = backendMessage(error);
  if (message !== null) {
    return <strong>{message}</strong>;
  }
  const detail = fallbackDetail(error);
  return (
    <>
      <strong>{GENERIC_ERROR}</strong>
      {detail !== null && <span className="state-card-detail">{detail}</span>}
    </>
  );
}

/**
 * Create class screen ("/admin/new-class"): name and total seats, checked in
 * the browser first, then POST /classes. Admin-only in spirit; there is no
 * login and no link to it in the navigation.
 *
 * - Success: a confirmation message, then a redirect to the class list.
 * - Failure (a 400 the browser-side checks did not catch, or the network): the
 *   message is shown inline and the student stays here with the form intact.
 */
export default function CreateClassPage() {
  const navigate = useNavigate();
  const create = useCreateClass();

  const [name, setName] = useState("");
  const [totalSeats, setTotalSeats] = useState("");
  // True when the number field holds text the browser cannot read as a number.
  const [seatsUnreadable, setSeatsUnreadable] = useState(false);
  // Errors only appear after the first attempt to submit, then follow the input.
  const [submitted, setSubmitted] = useState(false);

  const nameRef = useRef<HTMLInputElement>(null);
  const seatsRef = useRef<HTMLInputElement>(null);
  // Set the moment a request goes out, not when React next renders. `locked`
  // below only turns true after a re-render, and TanStack Query reports
  // `isPending` a tick after `mutate`, so a second submit arriving in between
  // would otherwise create a second class (POST /classes is not idempotent).
  const sending = useRef(false);

  const checked = validateClassForm({ name, totalSeats, seatsUnreadable });
  const errors: ClassFormErrors = submitted && !checked.ok ? checked.errors : {};

  // After a success the form is read-only until the redirect: a second click
  // must not create a second class.
  const locked = create.isPending || create.isSuccess;

  useEffect(() => {
    if (!create.isSuccess) {
      return;
    }
    const timer = window.setTimeout(() => navigate("/"), REDIRECT_DELAY_MS);
    // Leaving the page by hand first must not be undone by a late redirect.
    return () => window.clearTimeout(timer);
  }, [create.isSuccess, navigate]);

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (locked || sending.current) {
      return;
    }
    setSubmitted(true);
    if (!checked.ok) {
      // Take the keyboard to the first field that needs attention.
      (checked.errors.name !== undefined ? nameRef : seatsRef).current?.focus();
      return;
    }
    sending.current = true;
    // On success the latch stays shut: the form is locked until the redirect.
    // After a failure it opens again so the student can fix the values and retry.
    create.mutate(checked.request, {
      onError: () => {
        sending.current = false;
      },
    });
  }

  // Editing after a failed request: the old server message is about the old
  // values, so it goes away.
  function clearServerError() {
    if (create.isError) {
      create.reset();
    }
  }

  const created = create.data;

  return (
    <section className="create-class" aria-labelledby="create-class-title">
      <header className="create-class-head">
        <p className="create-class-kicker">Admin</p>
        <h1 id="create-class-title">Create a class</h1>
        <p className="create-class-lead">
          Add a class for students to apply to. Seats are held first come, first served.
        </p>
      </header>

      <form className="card create-class-form" onSubmit={handleSubmit} noValidate>
        <fieldset className="create-class-fields" disabled={locked}>
          <legend className="visually-hidden">Class details</legend>

          <div className="field">
            <label className="field-label" htmlFor="class-name">
              Class name
            </label>
            <input
              ref={nameRef}
              className="field-input"
              id="class-name"
              name="name"
              type="text"
              autoComplete="off"
              placeholder="Class 6 - Morning Shift"
              value={name}
              required
              aria-invalid={errors.name !== undefined}
              aria-describedby={
                errors.name !== undefined ? "class-name-hint class-name-error" : "class-name-hint"
              }
              onChange={(event) => {
                setName(event.target.value);
                clearServerError();
              }}
            />
            <p className="field-hint" id="class-name-hint">
              Shown to students, for example "Class 6 - Morning Shift".
            </p>
            {errors.name !== undefined && (
              <p className="field-error" id="class-name-error">
                {errors.name}
              </p>
            )}
          </div>

          <div className="field">
            <label className="field-label" htmlFor="class-total-seats">
              Total seats
            </label>
            <input
              ref={seatsRef}
              className="field-input create-class-seats"
              id="class-total-seats"
              name="totalSeats"
              type="number"
              inputMode="numeric"
              min={1}
              step={1}
              autoComplete="off"
              placeholder="120"
              value={totalSeats}
              required
              aria-invalid={errors.totalSeats !== undefined}
              aria-describedby={
                errors.totalSeats !== undefined
                  ? "class-seats-hint class-seats-error"
                  : "class-seats-hint"
              }
              onChange={(event) => {
                setTotalSeats(event.target.value);
                setSeatsUnreadable(event.target.validity.badInput);
                clearServerError();
              }}
            />
            <p className="field-hint" id="class-seats-hint">
              A whole number greater than 0.
            </p>
            {errors.totalSeats !== undefined && (
              <p className="field-error" id="class-seats-error">
                {errors.totalSeats}
              </p>
            )}
          </div>
        </fieldset>

        {create.isError && (
          <div className="state-card state-card--error create-class-alert" role="alert">
            <span className="state-card-icon">
              <AlertIcon />
            </span>
            <p className="state-card-body">
              <CreateErrorText error={create.error} />
            </p>
          </div>
        )}

        {created !== undefined && (
          <div className="state-card state-card--success create-class-alert" role="status">
            <span className="state-card-icon">
              <CheckIcon />
            </span>
            <p className="state-card-body">
              <strong>Class created.</strong>
              <span className="state-card-detail">
                {created.name} now has {created.totalSeats}{" "}
                {created.totalSeats === 1 ? "seat" : "seats"} open. Taking you to the class
                list…
              </span>
            </p>
          </div>
        )}

        <div className="create-class-actions">
          <button
            type="submit"
            className="btn btn--primary"
            disabled={locked}
            aria-busy={create.isPending}
          >
            {create.isPending ? "Creating…" : "Create class"}
            {create.isPending ? (
              <span className="spinner" aria-hidden="true" />
            ) : (
              <ArrowRightIcon className="btn-icon" />
            )}
          </button>
          <Link className="create-class-back" to="/">
            {created !== undefined ? "Go to the class list now" : "Back to classes"}
          </Link>
        </div>
      </form>
    </section>
  );
}
