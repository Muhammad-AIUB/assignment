import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import "./styles/index.css";
import Root from "./Root";

const rootElement = document.getElementById("root");
if (!rootElement) {
  throw new Error('index.html has no <div id="root"> to mount the app into.');
}

createRoot(rootElement).render(
  <StrictMode>
    <Root />
  </StrictMode>,
);
