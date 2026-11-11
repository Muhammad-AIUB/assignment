import axios from "axios";
import type { CSSProperties } from "react";
import { useApplyForClass } from "../api/useApplyForClass";
import { useClassList, type ClassListState } from "../api/useClassList";
import type { ApiErrorBody, SchoolClass } from "../api/types";
import { AlertIcon, ArrowRightIcon, InboxIcon } from "../components/icons";
import { MISSING_STUDENT_ID_MESSAGE, useRequireStudentId } from "../identity/requireStudentId";
import { seatStatus, splitClassName, type SeatStatus } from "./classSeats";
import SeatStrip from "./SeatStrip";
import "./ClassCards.css";

/** Fallback wording from docs/FRONTEND_SPEC.md ("Error handling"). */
const GENERIC_ERROR = "কিছু একটা ভুল হয়েছে, আবার চেষ্টা করুন";

const STATUS_LABEL: Record<SeatStatus, string> = {
  open: "Open",
  filling: "Filling fast",
  full: "Full",
};

/** The `message` of the backend's `{ error, message }` body, if the error has one. */
function backendMessage(error: unknown): string | null {
  if (axios.isAxiosError<ApiErrorBody>(error)) {
    const message = error.response?.data?.message;
    if (typeof message === "string" && message.trim() !== "") {
      return message;
    }
  }
  return null;
}

/**
 * One plain-language line saying what went wrong, or null when there is nothing
 * more to add than the generic message. Prefers the backend's own `message`.
 * Local to this screen for now; the error-handling session centralizes it.
 */
function errorDetail(error: unknown): string | null {
  const message = backendMessage(error);
  if (message !== null) {
    return message;
  }
  if (axios.isAxiosError(error) && !error.response) {
    return "Could not reach the server.";
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
      <strong lang="bn">{GENERIC_ERROR}</strong>
      {detail !== null && <span className="state-card-detail">{detail}</span>}
    </>
  );
}

/**
 * Why an apply failed, in the backend's own words (for example the 409 for no
 * free seat, or for an application the student already has in this class). When
 * there is no backend message (server unreachable, an unexpected failure) it
 * falls back to the generic line plus whatever detail there is.
 */
function ApplyErrorText({ error }: { error: unknown }) {
  const message = backendMessage(error);
  if (message !== null) {
    return <span>{message}</span>;
  }
  return <ErrorText error={error} />;
}

/** How a card's position in the grid staggers its entrance animation. */
function indexStyle(index: number): CSSProperties {
  return { "--i": index } as CSSProperties;
}

function ClassCard({ schoolClass, index }: { schoolClass: SchoolClass; index: number }) {
  const apply = useApplyForClass();
  // Applying needs a registration number. Without one nothing is sent: the card says what
  // to do and the cursor goes to the field, instead of the backend's header wording.
  const requireId = useRequireStudentId();
  const status = seatStatus(schoolClass);
  const full = status === "full";
  const { title, subtitle } = splitClassName(schoolClass.name);

  return (
    <li className="class-card" data-status={status} style={indexStyle(index)}>
      <div className="class-card-main">
        <div className="class-card-top">
          <h3 className="class-card-name">
            {title}
            {subtitle !== null && (
              <span className="class-card-sub">
                <span className="visually-hidden"> - </span>
                {subtitle}
              </span>
            )}
          </h3>
          <span className={`badge badge--${status}`}>{STATUS_LABEL[status]}</span>
        </div>

        <div className="class-card-count">
          <span className="class-card-avail">
            <span key={schoolClass.availableSeats} className="tick">
              {schoolClass.availableSeats}
            </span>
          </span>
          <span className="class-card-count-text">
            seats left
            <span className="class-card-of"> of {schoolClass.totalSeats}</span>
          </span>
        </div>

        <SeatStrip schoolClass={schoolClass} />

        <ul className="seat-legend">
          <li>
            <span className="swatch swatch--confirmed" aria-hidden="true" />
            <b>{schoolClass.confirmedSeats}</b> confirmed
          </li>
          <li>
            <span className="swatch swatch--held" aria-hidden="true" />
            <b>{schoolClass.heldSeats}</b> on hold
          </li>
          <li>
            <span className="swatch swatch--free" aria-hidden="true" />
            <b>{schoolClass.availableSeats}</b> available
          </li>
        </ul>
      </div>

      <div className="class-card-tear" aria-hidden="true" />

      <div className="class-card-stub">
        {/* Disabled while the request is in flight so a double click cannot
            send two applications. On success the hook navigates away. */}
        <button
          type="button"
          className="btn btn--primary class-card-apply"
          disabled={full || apply.isPending}
          aria-busy={apply.isPending}
          aria-label={`আবেদন করুন: ${schoolClass.name}`}
          onClick={() => {
            if (requireId.ensure()) apply.mutate(schoolClass.id);
          }}
        >
          <span lang="bn">আবেদন করুন</span>
          {apply.isPending ? (
            <span className="spinner" aria-hidden="true" />
          ) : (
            <ArrowRightIcon className="btn-icon" />
          )}
        </button>

        {requireId.missing && (
          <p className="class-card-error" role="alert">
            <AlertIcon />
            <span className="class-card-error-text">{MISSING_STUDENT_ID_MESSAGE}</span>
          </p>
        )}

        {apply.isError && (
          <p className="class-card-error" role="alert">
            <AlertIcon />
            {/* One block: with no backend message ApplyErrorText returns a headline
                and a detail line, which must stack beside the icon, not join its row. */}
            <span className="class-card-error-text">
              <ApplyErrorText error={apply.error} />
            </span>
          </p>
        )}

        {full && !apply.isError && (
          <p className="class-card-note">
            {schoolClass.heldSeats > 0
              ? "Seats on hold may reopen shortly."
              : "There are no seats left in this class."}
          </p>
        )}
      </div>
    </li>
  );
}

/** Placeholder cards for the very first load, in the real cards' shape. */
function SkeletonGrid() {
  return (
    <div role="status">
      <span className="visually-hidden">Loading classes…</span>
      <ul className="class-grid" aria-hidden="true">
        {[0, 1, 2].map((n) => (
          <li key={n} className="class-card class-card--skeleton">
            <div className="class-card-main">
              <span className="bone bone--title" />
              <span className="bone bone--sub" />
              <span className="bone bone--number" />
              <span className="bone bone--strip" />
            </div>
            <div className="class-card-tear" />
            <div className="class-card-stub">
              <span className="bone bone--button" />
            </div>
          </li>
        ))}
      </ul>
    </div>
  );
}

const LIVE_TEXT = {
  live: "Live · updates automatically",
  stale: "Reconnecting · showing the last numbers",
  offline: "Offline · trying again",
  connecting: "Connecting…",
} as const;

function liveMode(state: ClassListState): keyof typeof LIVE_TEXT {
  if (state.kind === "ready") return state.refreshError !== null ? "stale" : "live";
  return state.kind === "error" ? "offline" : "connecting";
}

function Hero({ state }: { state: ClassListState }) {
  const mode = liveMode(state);
  const classes = state.kind === "ready" ? state.classes : [];
  const seatsOpen = classes.reduce((sum, c) => sum + c.availableSeats, 0);

  return (
    <section className="hero" aria-labelledby="hero-title">
      <div className="hero-art" aria-hidden="true">
        <span className="hero-sun" />
      </div>

      <div className="hero-inner">
        <div className="hero-eyebrow">
          <p className="hero-kicker">
            <span lang="bn">ভর্তি</span> School admission
          </p>
          <p className="live-pill" data-mode={mode}>
            <span className="live-dot" aria-hidden="true" />
            {LIVE_TEXT[mode]}
          </p>
        </div>

        <h1 id="hero-title">
          <span className="hero-title-line">Find your class.</span>
          <span className="hero-title-line hero-title-accent">Hold your seat.</span>
        </h1>

        <p className="hero-lead">
          Seats go first come, first served. Apply to hold one, then pay the
          admission fee in time to make it yours.
        </p>

        {classes.length > 0 && (
          <dl className="hero-stats">
            <div>
              <dt>Seats open now</dt>
              <dd>
                <span key={seatsOpen} className="tick">
                  {seatsOpen}
                </span>
              </dd>
            </div>
            <div>
              <dt>Classes</dt>
              <dd>{classes.length}</dd>
            </div>
          </dl>
        )}
      </div>
    </section>
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
    <>
      <Hero state={state} />

      <section className="classes" aria-labelledby="classes-title">
        <div className="classes-head">
          <h2 id="classes-title">Choose a class</h2>
          {state.kind === "ready" && state.classes.length > 0 && (
            <p className="classes-count">
              {state.classes.length} {state.classes.length === 1 ? "class" : "classes"}
            </p>
          )}
        </div>

        {state.kind === "loading" && <SkeletonGrid />}

        {state.kind === "error" && (
          <div className="state-card state-card--error" role="alert">
            <span className="state-card-icon">
              <AlertIcon />
            </span>
            <p className="state-card-body">
              <ErrorText error={state.error} />
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
                  <ErrorText error={state.refreshError} />
                  <span className="state-card-detail">
                    Showing the last seat counts received, which may be out of
                    date. Trying again automatically.
                  </span>
                </p>
              </div>
            )}

            {state.classes.length === 0 ? (
              <div className="state-card state-card--empty">
                <span className="state-card-icon">
                  <InboxIcon />
                </span>
                <p>No classes are open for admission yet.</p>
              </div>
            ) : (
              <ul className="class-grid">
                {state.classes.map((schoolClass, index) => (
                  <ClassCard key={schoolClass.id} schoolClass={schoolClass} index={index} />
                ))}
              </ul>
            )}
          </>
        )}
      </section>
    </>
  );
}
