import { Route, Routes } from "react-router-dom";
import AppLayout from "./components/AppLayout";
import ClassListPage from "./pages/ClassListPage";
import CreateClassPage from "./pages/CreateClassPage";
import ApplicationStatusPage from "./pages/ApplicationStatusPage";
import MyApplicationsPage from "./pages/MyApplicationsPage";

/**
 * The shell and the routes, without a router around them. <App> puts a
 * BrowserRouter around this; the tests put a MemoryRouter around the very same
 * routes (src/test/renderWithProviders.tsx), so a test exercises the real
 * route table instead of a copy of it.
 */
export default function AppRoutes() {
  return (
    <AppLayout>
      <Routes>
        <Route path="/" element={<ClassListPage />} />
        <Route path="/admin/new-class" element={<CreateClassPage />} />
        <Route path="/holds/:holdId" element={<ApplicationStatusPage />} />
        <Route path="/me" element={<MyApplicationsPage />} />
      </Routes>
    </AppLayout>
  );
}
