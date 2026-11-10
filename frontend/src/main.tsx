import { StrictMode } from "react";
import { createRoot } from "react-dom/client";
import { QueryClientProvider } from "@tanstack/react-query";
import App from "./App";
import { StudentIdProvider } from "./identity/StudentIdProvider";
import { queryClient } from "./queryClient";

const rootElement = document.getElementById("root");
if (!rootElement) {
  throw new Error('index.html has no <div id="root"> to mount the app into.');
}

createRoot(rootElement).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <StudentIdProvider>
        <App />
      </StudentIdProvider>
    </QueryClientProvider>
  </StrictMode>,
);
