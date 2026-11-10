import { BrowserRouter, Route, Routes } from "react-router-dom";
import AppLayout from "./components/AppLayout";
import ClassListPage from "./pages/ClassListPage";
import CreateClassPage from "./pages/CreateClassPage";
import ApplicationStatusPage from "./pages/ApplicationStatusPage";
import MyApplicationsPage from "./pages/MyApplicationsPage";

export default function App() {
  return (
    <BrowserRouter>
      <AppLayout>
        <Routes>
          <Route path="/" element={<ClassListPage />} />
          <Route path="/admin/new-class" element={<CreateClassPage />} />
          <Route path="/holds/:holdId" element={<ApplicationStatusPage />} />
          <Route path="/me" element={<MyApplicationsPage />} />
        </Routes>
      </AppLayout>
    </BrowserRouter>
  );
}
