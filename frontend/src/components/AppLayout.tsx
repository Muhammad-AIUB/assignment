import type { ReactNode } from "react";
import { Link } from "react-router-dom";
import StudentIdInput from "./StudentIdInput";
import "./AppLayout.css";

/**
 * Persistent shell: the header with the student identity input is rendered
 * around <Routes>, so it appears on every URL, including ones that match no
 * route, and keeps its state while the student moves between pages.
 */
export default function AppLayout({ children }: { children: ReactNode }) {
  return (
    <>
      <header className="app-header">
        <Link to="/" className="app-title">
          School Admission
        </Link>
        <StudentIdInput />
      </header>
      <main>{children}</main>
    </>
  );
}
