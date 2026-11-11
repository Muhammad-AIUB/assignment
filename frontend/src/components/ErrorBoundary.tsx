import { Component, type ErrorInfo, type ReactNode } from "react";
import ErrorFallback from "./ErrorFallback";

interface ErrorBoundaryProps {
  children: ReactNode;
  /**
   * While the fallback is showing, a change of this value clears it and renders the
   * children again. The shell passes the current path, so a student who goes to
   * another page leaves the failed screen behind instead of staying on its message.
   */
  resetKey?: unknown;
  /**
   * Called when the student presses "Try again", before the children render again.
   * The cause of a crash is often data the screen has cached, so the owner gets the
   * chance to throw that away first.
   */
  onReset?: () => void;
}

interface ErrorBoundaryState {
  failed: boolean;
}

/**
 * docs/FRONTEND_SPEC.md: "Wrap the app in a React error boundary so a render crash
 * never produces a fully blank page."
 *
 * Without one, an error thrown while rendering unmounts the whole React tree and
 * leaves an empty page. This catches it, shows a plain message with a way out
 * (Try again, or back to the classes), and leaves everything outside the boundary
 * (the header and the registration number input, when it sits inside the shell)
 * on screen.
 *
 * It only catches errors thrown while rendering. A failed request is not one; each
 * screen shows those itself.
 */
export default class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  state: ErrorBoundaryState = { failed: false };

  static getDerivedStateFromError(): ErrorBoundaryState {
    return { failed: true };
  }

  componentDidCatch(error: Error, info: ErrorInfo) {
    // A production build of React says nothing about an error a boundary caught, so
    // without this line the cause of a crash would leave no trace at all.
    console.error("[ErrorBoundary] a screen crashed:", error, info.componentStack);
  }

  componentDidUpdate(prevProps: ErrorBoundaryProps, prevState: ErrorBoundaryState) {
    // Only when the fallback was already up before this update. If resetKey changed in
    // the very update that crashed (the student navigated to a screen that fails), the
    // fallback must stay, not try the same children once more.
    if (prevState.failed && this.state.failed && prevProps.resetKey !== this.props.resetKey) {
      this.setState({ failed: false });
    }
  }

  private retry = () => {
    this.props.onReset?.();
    this.setState({ failed: false });
  };

  render() {
    return this.state.failed ? <ErrorFallback onRetry={this.retry} /> : this.props.children;
  }
}
