import { useEffect } from "react";
import { useAuthStore } from "./app/store/authStore";
import { AUTH_CLEARED_EVENT } from "./lib/api";
import { Shell } from "./features/shell/Shell";
import { LoginPage } from "./features/auth/LoginPage";
import { OwnerSetupPage } from "./features/auth/OwnerSetupPage";
import { PortalPage } from "./features/portal/PortalPage";

function App() {
  const status = useAuthStore((s) => s.status);
  const user = useAuthStore((s) => s.user);
  const loadMe = useAuthStore((s) => s.loadMe);

  useEffect(() => {
    loadMe();
  }, [loadMe]);

  useEffect(() => {
    const onAuthCleared = () => loadMe();
    window.addEventListener(AUTH_CLEARED_EVENT, onAuthCleared);
    return () => window.removeEventListener(AUTH_CLEARED_EVENT, onAuthCleared);
  }, [loadMe]);

  // Standalone, never linked from the sidebar — reached only by typing the URL directly.
  if (window.location.pathname === "/owner-setup") {
    return <OwnerSetupPage />;
  }

  // The public customer portal — anonymous, the token in the URL is the credential.
  if (window.location.pathname.startsWith("/portal/")) {
    return <PortalPage />;
  }

  if (status !== "ready") {
    return <div className="grid h-full place-items-center bg-(--void) text-(--ice-3)">در حال بارگذاری…</div>;
  }

  return user ? <Shell /> : <LoginPage />;
}

export default App;
