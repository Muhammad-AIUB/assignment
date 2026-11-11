import { Link } from "react-router-dom";
import { ArrowRightIcon } from "../components/icons";
import "./NotFoundPage.css";

/**
 * The catch-all route: an address that matches no screen. Without it a mistyped or
 * outdated link rendered the shell with an empty page and no way forward
 * (docs/FRONTEND_SPEC.md: no error may leave a blank screen).
 */
export default function NotFoundPage() {
  return (
    <section className="not-found" aria-labelledby="not-found-title">
      <h1 id="not-found-title">Page not found</h1>
      <p className="not-found-text">
        There is nothing at this address. Check the link, or start again from the classes.
      </p>
      <Link to="/" className="btn btn--primary">
        Browse classes <ArrowRightIcon className="btn-icon" />
      </Link>
    </section>
  );
}
