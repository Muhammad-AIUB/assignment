import axios from "axios";
import { Link, useParams } from "react-router-dom";
import { useHold } from "../api/useHold";
import { useCancelHold, useConfirmHold } from "../api/useHoldActions";
import type { ApiErrorBody, Hold, HoldStatus } from "../api/types";
import { AlertIcon, ArrowRightIcon, CheckIcon } from "../components/icons";
import { formatCountdown, parseInstant, resolveHold, URGENT_MS } from "./holdCountdown";
import { useNow } from "./useNow";
import "./ApplicationStatus.css";

/** Fallback wording from docs/FRONTEND_SPEC.md ("Error handling"). */
const GENERIC_ERROR = "কিছু একটা ভুল হয়েছে, আবার চেষ্টা করুন";

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

/** A plain line for the cases where the backend sent no message of its own. */
function fallbackDetail(error: unknown): string | null {
  if (axios.isAxiosError(error) && !error.response) {
    return "Could not reach the server.";
  }
  if (error instanceof Error && error.message.trim() !== "") {
    return error.message;
  }
  return null;
}

/**
 * Why a request failed, in the backend's own words (for a confirm or a cancel
 * that is a 409 such as "This hold has expired."). With no backend message
 * (server unreachable, an unexpected failure) it is the generic line plus
 * whatever detail there is. Local to this screen, like the class list's copy;
 * the error-handling session centralizes both.
 */
function ErrorText({ error }: { error: unknown }) {
  const message = backendMessage(error);
  if (message !== null) {
    return <span>{message}</span>;
  }
  const detail = fallbackDetail(error);
  return (
    <>
      <strong lang="bn">{GENERIC_ERROR}</strong>
      {detail !== null && <span className="hold-error-detail">{detail}</span>}
    </>
  );
}

const COPY: Record<HoldStatus, { badge: string; title: string; text: string }> = {
  HELD: {
    badge: "On hold",
    title: "Your seat is on hold",
    text: "Pay the admission fee before the timer runs out, or the seat goes back to the class.",
  },
  CONFIRMED: {
    badge: "Confirmed",
    title: "Admission confirmed",
    text: "Your seat is secured. There is nothing more to do.",
  },
  EXPIRED: {
    badge: "Expired",
    title: "This hold has expired",
    text: "The time ran out and the seat was released. Go back to the classes to apply again.",
  },
  CANCELLED: {
    badge: "Cancelled",
    title: "Application cancelled",
    text: "This application was cancelled and its seat was released.",
  },
};

const BADGE_CLASS: Record<HoldStatus, string> = {
  HELD: "badge--filling",
  CONFIRMED: "badge--open",
  EXPIRED: "badge--full",
  CANCELLED: "badge--full",
};

function Skeleton() {
  return (
    <div role="status">
      <span className="visually-hidden">Loading your application…</span>
      <div className="hold-card" aria-hidden="true">
        <div className="hold-head">
          <span className="bone" style={{ height: "2rem", width: "60%" }} />
        </div>
        <div className="hold-panel hold-panel--loading">
          <span className="bone" style={{ height: "4rem", width: "55%", margin: "0 auto" }} />
        </div>
        <div className="hold-body">
          <span className="bone" style={{ height: "3rem", width: "100%" }} />
        </div>
      </div>
    </div>
  );
}

function ErrorCard({ error, notFound }: { error: unknown; notFound: boolean }) {
  return (
    <div className="state-card state-card--error" role="alert">
      <span className="state-card-icon">
        <AlertIcon />
      </span>
      <p className="state-card-body">
        {notFound ? <strong>We could not find this application.</strong> : <ErrorText error={error} />}
        {notFound ? (
          <span className="state-card-detail">
            {backendMessage(error) ?? "Check the link, or look for it under My applications."}
          </span>
        ) : (
          <span className="state-card-detail">Trying again automatically.</span>
        )}
      </p>
    </div>
  );
}

function HoldDetails({
  holdId,
  hold,
  fetchedAt,
  refreshError,
}: {
  holdId: string;
  hold: Hold;
  fetchedAt: number;
  refreshError: unknown;
}) {
  const expiresAtMs = parseInstant(hold.expiresAt);
  // The timers only run while the server still says HELD: every other status is
  // final, so there is no deadline left to watch.
  const deadline = hold.status === "HELD" && !Number.isNaN(expiresAtMs) ? expiresAtMs : null;
  const now = useNow(deadline);
  const view = resolveHold({
    serverStatus: hold.status,
    expiresAtMs,
    nowMs: now,
    fetchedAtMs: fetchedAt,
  });

  const confirm = useConfirmHold(holdId);
  const cancel = useCancelHold(holdId);
  const busy = confirm.isPending || cancel.isPending;

  const held = view.status === "HELD";
  // Confirm needs a live countdown as well as a HELD status; cancel only the status.
  const canConfirm = held && !view.countdownDone && !busy;
  const canCancel = held && !busy;
  const actionError = confirm.isError ? confirm.error : cancel.isError ? cancel.error : null;

  // Starting one action clears the other's old failure, so at most one message shows.
  const onConfirm = () => {
    cancel.reset();
    confirm.mutate();
  };
  const onCancel = () => {
    confirm.reset();
    cancel.mutate();
  };

  const copy = COPY[view.status];
  const urgent = held && view.remainingMs !== null && view.remainingMs <= URGENT_MS;
  const endsAt = Number.isNaN(expiresAtMs)
    ? null
    : new Date(expiresAtMs).toLocaleTimeString([], { hour: "numeric", minute: "2-digit", second: "2-digit" });

  return (
    <>
      {refreshError !== null && (
        <div className="state-card state-card--warn" role="status">
          <span className="state-card-icon">
            <AlertIcon />
          </span>
          <p className="state-card-body">
            <ErrorText error={refreshError} />
            <span className="state-card-detail">
              Showing the last status received, which may be out of date. Trying again automatically.
            </span>
          </p>
        </div>
      )}

      <article className="hold-card" data-status={view.status} aria-labelledby="hold-title">
        <header className="hold-head">
          <h1 id="hold-title">{copy.title}</h1>
          <span className={`badge ${BADGE_CLASS[view.status]}`}>{copy.badge}</span>
        </header>

        <div className="hold-panel" data-urgent={urgent}>
          {held ? (
            <>
              {view.remainingMs === null ? (
                <span className="hold-clock">--:--</span>
              ) : (
                <span className="hold-clock" role="timer" aria-label="Time left to pay the admission fee">
                  {formatCountdown(view.remainingMs)}
                </span>
              )}
              <span className="hold-panel-caption">
                {view.serverDisagrees
                  ? "Time is up on this device. Checking with the server…"
                  : "left to pay the admission fee"}
              </span>
            </>
          ) : (
            <>
              <span className="hold-seal" aria-hidden="true">
                {view.status === "CONFIRMED" ? <CheckIcon /> : <AlertIcon />}
              </span>
              <span className="hold-panel-caption">{copy.badge}</span>
            </>
          )}
        </div>

        <div className="hold-body">
          <p className="hold-text">{copy.text}</p>

          <div className="hold-actions">
            <button
              type="button"
              className="btn btn--primary hold-confirm"
              disabled={!canConfirm}
              aria-busy={confirm.isPending}
              onClick={onConfirm}
            >
              <span lang="bn">ভর্তি ফি দিন</span>
              {confirm.isPending ? (
                <span className="spinner" aria-hidden="true" />
              ) : (
                <ArrowRightIcon className="btn-icon" />
              )}
            </button>
            <button
              type="button"
              className="btn hold-cancel"
              disabled={!canCancel}
              aria-busy={cancel.isPending}
              onClick={onCancel}
            >
              <span lang="bn">বাতিল</span>
            </button>
          </div>

          {actionError !== null && (
            <p className="hold-error" role="alert">
              <AlertIcon />
              {/* One block: with no backend message ErrorText returns a headline and
                  a detail line, which must stack beside the icon, not join its row. */}
              <span className="hold-error-text">
                <ErrorText error={actionError} />
              </span>
            </p>
          )}

          {(view.status === "EXPIRED" || view.status === "CANCELLED") && (
            <Link to="/" className="hold-next">
              Browse classes <ArrowRightIcon />
            </Link>
          )}
          {view.status === "CONFIRMED" && (
            <Link to="/me" className="hold-next">
              My applications <ArrowRightIcon />
            </Link>
          )}

          <dl className="hold-meta">
            <div>
              <dt>Application</dt>
              <dd>{hold.holdId}</dd>
            </div>
            <div>
              <dt>Class</dt>
              <dd>{hold.classId}</dd>
            </div>
            {endsAt !== null && (
              <div>
                <dt>Hold ends</dt>
                <dd>{endsAt}</dd>
              </div>
            )}
          </dl>
        </div>

        {/* Announces a change of status (not the ticking countdown) to screen readers. */}
        <p className="visually-hidden" role="status" aria-live="polite">
          {copy.title}
        </p>
      </article>
    </>
  );
}

function HoldScreen({ holdId }: { holdId: string }) {
  const state = useHold(holdId);

  return (
    <section className="hold">
      {state.kind === "loading" && <Skeleton />}
      {state.kind === "error" && <ErrorCard error={state.error} notFound={state.notFound} />}
      {state.kind === "ready" && (
        <HoldDetails
          holdId={holdId}
          hold={state.hold}
          fetchedAt={state.fetchedAt}
          refreshError={state.refreshError}
        />
      )}
    </section>
  );
}

/**
 * Application status screen ("/holds/:holdId"): the hold's status, a live
 * countdown to its deadline, and the confirm ("ভর্তি ফি দিন") and cancel
 * ("বাতিল") actions.
 *
 * The status is polled (see useHold). The countdown runs on its own clock and
 * turns the screen to EXPIRED the moment it reaches zero, without waiting for the
 * next poll; the server's answer decides again as soon as one arrives (see
 * resolveHold).
 */
export default function ApplicationStatusPage() {
  const { holdId } = useParams<{ holdId: string }>();
  if (holdId === undefined || holdId === "") {
    return (
      <section className="hold">
        <ErrorCard error={null} notFound />
      </section>
    );
  }
  // Keyed by id so moving from one hold to another starts from a clean state.
  return <HoldScreen key={holdId} holdId={holdId} />;
}
