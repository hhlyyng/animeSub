import { useEffect, useState, useCallback } from "react";
import { BrowserRouter as Router, Routes, Route, Navigate } from "react-router-dom";
import { useAppStore } from "./stores/useAppStores";
import * as authApi from "./services/authApi";
import LoginBlock from "./features/auth/components/LoginBlock";
import SetupPage from "./features/setup/SetupPage";
import HomePage from "./features/home/layout/HomePage";

function ProtectedRoute({ children }: { children: React.ReactNode }) {
  const token = useAppStore((state) => state.token);
  if (!token) {
    return <Navigate to="/login" replace />;
  }
  return <>{children}</>;
}

export default function App() {
  const token = useAppStore((state) => state.token);
  const [authChecked, setAuthChecked] = useState(false);
  const [needsSetup, setNeedsSetup] = useState(false);

  useEffect(() => {
    const checkAuth = async () => {
      try {
        const status = await authApi.getAuthStatus(token);
        if (!status.isSetupCompleted) {
          setNeedsSetup(true);
        }
      } catch {
        // If auth check fails, continue — routing will handle it
      } finally {
        setAuthChecked(true);
      }
    };
    void checkAuth();
  }, []);

  const onSetupComplete = useCallback(() => {
    setNeedsSetup(false);
  }, []);

  if (!authChecked) {
    return (
      <div className="flex h-screen w-screen items-center justify-center bg-gray-50">
        <div className="h-8 w-8 animate-spin rounded-full border-2 border-gray-300 border-t-gray-800" />
      </div>
    );
  }

  return (
    <Router>
      <Routes>
        <Route path="/setup" element={<SetupPage onSetupComplete={onSetupComplete} />} />
        <Route path="/login" element={
          needsSetup ? <Navigate to="/setup" replace /> : <LoginBlock />
        } />
        <Route path="/*" element={
          needsSetup
            ? <Navigate to="/setup" replace />
            : <ProtectedRoute><HomePage /></ProtectedRoute>
        } />
      </Routes>
    </Router>
  );
}
