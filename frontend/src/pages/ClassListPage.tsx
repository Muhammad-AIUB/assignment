import axios from "axios";
import { useClassList } from "../api/useClassList";
import type { ApiErrorBody, SchoolClass } from "../api/types";
import "./ClassListPage.css";

/** Fallback wording from docs/FRONTEND_SPEC.md ("Error handling"). */
const GENERIC_ERROR = "কিছু একটা ভুল হয়েছে, আবার চেষ্টা করুন";

/**
 * One plain-language line saying what went wrong, or null when there is nothing
 * more to add than the generic message. Prefers the backend's own `message`.
 * Local to this screen for now; the error-handling session centralizes it.
 */
function errorDetail(error: unknown): string | null {
  if (axios.isAxiosError<ApiErrorBody>(error)) {
    const message = error.response?.data?.message;
    if (typeof message === "string" && message.trim() !== "") {
      return message;
    }
    if (!error.response) {
      return "Could not reach the server.";
    }
  }
  if (error instanceof Error && error.message.trim() !== "") {
    return error.message;
  }
  return null;
}

function ErrorText({ error }: { error: unknown }) {
  const detail = errorDetail(error);
  return (
    <>
      <strong>{GENERIC_ERROR}</strong>
      {detail !== null && <span className="class-list-detail">{detail}</span>}
    </>
  );
}

function handleApply(schoolClass: SchoolClass) {
  // TODO(Session F4, apply flow): call createHold(schoolClass.id) and go to the
  // application status screen. Intentionally not wired to the API yet.
  console.log("TODO(Session F4): apply for class", schoolClass.id);
}

function ClassRow({ schoolClass }: { schoolClass: SchoolClass }) {
  const full = schoolClass.availableSeats <= 0;
  return (
    <tr>
      <th scope="row">{schoolClass.name}</th>
      <td className="class-list-num">{schoolClass.totalSeats}</td>
      <td className="class-list-num">{schoolClass.heldSeats}</td>
      <td className="class-list-num">{schoolClass.confirmedSeats}</td>
      <td className="class-list-num">
        {schoolClass.availableSeats}
        {full && <span className="class-list-full">Full</span>}
      </td>
      <td>
        <button
          type="button"
          disabled={full}
          aria-label={`আবেদন করুন: ${schoolClass.name}`}
          onClick={() => handleApply(schoolClass)}
        >
          আবেদন করুন
        </button>
      </td>
    </tr>
  );
}

/**
 * Class list screen ("/"): every class with its seat counts and an apply
 * button. The data refreshes itself (see useClassList); a loading state is shown
 * on the first load only, and an error is always drawn, never a blank page.
 */
export default function ClassListPage() {
  const state = useClassList();

  return (
    <section className="class-list">
      <h1>Classes</h1>
      <p className="class-list-note">Seat counts update automatically.</p>

      {state.kind === "loading" && <p role="status">Loading classes…</p>}

      {state.kind === "error" && (
        <p className="class-list-error" role="alert">
          <ErrorText error={state.error} />
          <span className="class-list-detail">
            Trying again automatically.
          </span>
        </p>
      )}

      {state.kind === "ready" && (
        <>
          {state.refreshError !== null && (
            <p className="class-list-banner" role="status">
              <ErrorText error={state.refreshError} />
              <span className="class-list-detail">
                Showing the last seat counts received, which may be out of
                date. Trying again automatically.
              </span>
            </p>
          )}

          {state.classes.length === 0 ? (
            <p>No classes are open for admission yet.</p>
          ) : (
            <div className="class-list-table-wrap">
              <table className="class-list-table">
                <caption className="class-list-hidden">
                  Classes open for admission
                </caption>
                <thead>
                  <tr>
                    <th scope="col">Class</th>
                    <th scope="col" className="class-list-num">
                      Total seats
                    </th>
                    <th scope="col" className="class-list-num">
                      Held
                    </th>
                    <th scope="col" className="class-list-num">
                      Confirmed
                    </th>
                    <th scope="col" className="class-list-num">
                      Available
                    </th>
                    <th scope="col">
                      <span className="class-list-hidden">Apply</span>
                    </th>
                  </tr>
                </thead>
                <tbody>
                  {state.classes.map((schoolClass) => (
                    <ClassRow key={schoolClass.id} schoolClass={schoolClass} />
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </>
      )}
    </section>
  );
}
