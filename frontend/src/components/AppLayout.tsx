import type { ReactNode } from "react";
import { Link, NavLink } from "react-router-dom";
import { BrandMark } from "./icons";
import StudentIdInput from "./StudentIdInput";
import "./AppLayout.css";

/**
 * Persistent shell: the header with the student identity input is rendered
 * around <Routes>, so it appears on every URL, including ones that match no
 * route, and keeps its state while the student moves between pages.
 *
 * Layout convention (see AppLayout.css): every direct child of <main> is a
 * centred column, except an element with the `hero` class, which runs the full
 * width of the page and manages its own inner column.
 */
export default function AppLayout({ children }: { children: ReactNode }) {
  return (
    <>
      <a className="skip-link" href="#main">
        Skip to content
      </a>

      <header className="app-header">
        <div className="app-header-bar">
          <Link to="/" className="brand">
            <BrandMark className="brand-mark" />
            <span className="brand-name">School Admission</span>
          </Link>
          <nav className="app-nav" aria-label="Main">
            <NavLink to="/" end>
              Classes
            </NavLink>
            <NavLink to="/me">My applications</NavLink>
          </nav>
        </div>

        <div className="identity-bar">
          <div className="identity-bar-inner">
            <StudentIdInput />
          </div>
        </div>
      </header>

      <main id="main" className="app-main">
        {children}
      </main>

      <footer className="app-footer">
        <div className="app-footer-inner">
          <span className="app-footer-brand">School Admission</span>
          <span>
            Seats are held first come, first served. A hold that is not paid in time is released.
          </span>
        </div>
      </footer>
    </>
  );
}
