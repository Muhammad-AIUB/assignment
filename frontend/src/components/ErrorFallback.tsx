import { AlertIcon, ArrowRightIcon } from "./icons";
import "./ErrorFallback.css";

/**
 * What the student sees when a screen has crashed (see ErrorBoundary): a plain
 * message explaining what happened, and two ways out.
 *
 * It must work when almost nothing else does, so it uses no router, no query
 * client and no context: "Back to classes" is an ordinary link, which loads the
 * page again from scratch and so also clears whatever state caused the crash.
 */
export default function ErrorFallback({ onRetry }: { onRetry: () => void }) {
  return (
    <section className="crash" role="alert" aria-labelledby="crash-title">
      <span className="crash-icon" aria-hidden="true">
        <AlertIcon />
      </span>
      <h1 id="crash-title">Something went wrong</h1>
      <p className="crash-text">This screen stopped working. Try again, or go back to the classes.</p>
      <div className="crash-actions">
        <button type="button" className="btn btn--primary" onClick={onRetry}>
          Try again
        </button>
        <a className="btn" href="/">
          Back to classes <ArrowRightIcon className="btn-icon" />
        </a>
      </div>
    </section>
  );
}
