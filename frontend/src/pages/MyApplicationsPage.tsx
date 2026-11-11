import type { CSSProperties } from "react";
import { Link } from "react-router-dom";
import { useMyHolds } from "../api/useMyHolds";
import type { HoldStatus, MyHold } from "../api/types";
import ApiErrorText from "../components/ApiErrorText";
import { AlertIcon, ArrowRightIcon, IdCardIcon, InboxIcon } from "../components/icons";
import { parseInstant } from "./holdCountdown";
import "./MyApplications.css";

// The words and badge colours for each status. They match the application status
// screen (ApplicationStatusPage), so a student sees the same label in both places.
const BADGE: Record<HoldStatus, { label: string; className: string }> = {
  HELD: { label: "On hold", className: "badge--filling" },
  CONFIRMED: { label: "Confirmed", className: "badge--open" },
  EXPIRED: { label: "Expired", className: "badge--full" },
  CANCELLED: { label: "Cancelled", className: "badge--full" },
};

const SUMMARY: Record<HoldStatus, string> = {
  HELD: "Pay the admission fee in time, or the seat goes back to the class.",
  CONFIRMED: "Your seat is secured. There is nothing more to do.",
  EXPIRED: "The time ran out and the seat was released.",
  CANCELLED: "This application was cancelled and its seat was released.",
};

/** How a row's position in the list staggers its entrance animation. */
function indexStyle(index: number): CSSProperties {
  return { "--i": index } as CSSProperties;
}

/** The time of day a hold ends at, in the student's own clock and format; null if unreadable. */
function endTime(expiresAt: string): string | null {
  const ms = parseInstant(expiresAt);
  if (Number.isNaN(ms)) return null;
  return new Date(ms).toLocaleTimeString([], { hour: "numeric", minute: "2-digit", second: "2-digit" });
}

function ApplicationRow({ hold, index }: { hold: MyHold; index: number }) {
  const badge = BADGE[hold.status];
  // Only a hold that is still running has an end to tell about; for every other status
  // the time has no meaning any more.
  const ends = hold.status === "HELD" ? endTime(hold.expiresAt) : null;

  return (
    <li className="app-card" data-status={hold.status} style={indexStyle(index)}>
      <div className="app-card-main">
        <div className="app-card-top">
          <h2 className="app-card-name">{hold.className}</h2>
          <span className={`badge ${badge.className}`}>{badge.label}</span>
        </div>
        <p className="app-card-text">{SUMMARY[hold.status]}</p>
        {ends !== null && (
          <p className="app-card-ends">
            Hold ends <time dateTime={hold.expiresAt}>{ends}</time>
          </p>
        )}
      </div>

      {/* Told apart by class name, so a screen reader can tell the identical links apart. */}
      <Link
        to={`/holds/${encodeURIComponent(hold.holdId)}`}
        className="btn app-card-link"
        aria-label={`View status of ${hold.className}`}
      >
        View status <ArrowRightIcon className="btn-icon" />
      </Link>
    </li>
  );
}

/** Placeholder rows for the very first load, in the real rows' shape. */
function Skeleton() {
  return (
    <div role="status">
      <span className="visually-hidden">Loading your applications…</span>
      <ul className="app-list" aria-hidden="true">
        {[0, 1, 2].map((n) => (
          <li key={n} className="app-card app-card--skeleton">
            <div className="app-card-main">
              <span className="bone" style={{ height: "1.6rem", width: "55%" }} />
              <span className="bone" style={{ height: "1rem", width: "80%" }} />
            </div>
            <span className="bone" style={{ height: "3rem", width: "9rem" }} />
          </li>
        ))}
      </ul>
    </div>
  );
}

/**
 * My applications ("/me"): every application the saved registration number has, from
 * GET /me/holds, each row linking to its status screen. docs/FRONTEND_SPEC.md
 * ("Resilience"): nothing here lives in memory; after a refresh or a new visit the
 * list comes back from that saved number and the backend alone.
 *
 * The list refreshes itself (see useMyHolds). A loading state is shown on the first
 * load only, and every failure is drawn: never a blank page.
 */
export default function MyApplicationsPage() {
  const state = useMyHolds();

  return (
    <section className="my-apps" aria-labelledby="my-apps-title">
      <header className="my-apps-head">
        <h1 id="my-apps-title">My applications</h1>
        <p className="my-apps-lead">Every seat you have applied for, with its latest status.</p>
      </header>

      {state.kind === "needsStudentId" && (
        <div className="state-card state-card--empty">
          <span className="state-card-icon">
            <IdCardIcon />
          </span>
          <p>Enter your registration number to see your applications.</p>
        </div>
      )}

      {state.kind === "loading" && <Skeleton />}

      {state.kind === "error" && (
        <div className="state-card state-card--error" role="alert">
          <span className="state-card-icon">
            <AlertIcon />
          </span>
          <p className="state-card-body">
            <ApiErrorText error={state.error} />
            <span className="state-card-detail">Trying again automatically.</span>
          </p>
        </div>
      )}

      {state.kind === "ready" && (
        <>
          {state.refreshError !== null && (
            <div className="state-card state-card--warn" role="status">
              <span className="state-card-icon">
                <AlertIcon />
              </span>
              <p className="state-card-body">
                <ApiErrorText error={state.refreshError} />
                <span className="state-card-detail">
                  Showing the last list received, which may be out of date. Trying again automatically.
                </span>
              </p>
            </div>
          )}

          {state.holds.length === 0 ? (
            <div className="state-card state-card--empty">
              <span className="state-card-icon">
                <InboxIcon />
              </span>
              <div className="state-card-body">
                <p>You have not applied to any class yet.</p>
                <Link to="/" className="my-apps-browse">
                  Browse classes <ArrowRightIcon />
                </Link>
              </div>
            </div>
          ) : (
            <ul className="app-list" aria-label="Your applications">
              {state.holds.map((hold, index) => (
                <ApplicationRow key={hold.holdId} hold={hold} index={index} />
              ))}
            </ul>
          )}
        </>
      )}
    </section>
  );
}
