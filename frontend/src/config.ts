// Runtime configuration.
//
// Values come from `window.__APP_CONFIG__`, set by /config.js, a plain file in
// public/ that index.html loads with a <script> tag before the app bundle.
// They are deliberately NOT read from `import.meta.env`: Vite inlines that at
// build time, which would bake the backend URL into the bundle. See
// docs/ARCHITECTURE.md ("Runtime config").

export interface AppConfig {
  /** Base URL of the backend API, e.g. "http://localhost:8080". */
  apiBaseUrl: string;
}

declare global {
  interface Window {
    __APP_CONFIG__?: Partial<AppConfig>;
  }
}

/**
 * Backend base URL, read from `window.__APP_CONFIG__` at call time (not at
 * import time), without a trailing slash.
 *
 * Throws a descriptive error when the config is missing, so a deployment where
 * /config.js was not served (or was answered with an HTML fallback page) shows
 * the cause instead of requests going to the wrong place.
 */
export function getApiBaseUrl(): string {
  const value = window.__APP_CONFIG__?.apiBaseUrl;
  if (typeof value !== "string" || value.trim() === "") {
    throw new Error(
      "window.__APP_CONFIG__.apiBaseUrl is not set. Check that /config.js is " +
        "served and loaded before the app bundle (see index.html).",
    );
  }
  return value.trim().replace(/\/+$/, "");
}
