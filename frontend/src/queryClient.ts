import { QueryClient } from "@tanstack/react-query";

/** Single app-wide TanStack Query client, provided in main.tsx. */
export const queryClient = new QueryClient();
