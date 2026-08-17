import { useEffect } from "react";
import { useAuthStore } from "./app/store/authStore";
import { AUTH_CLEARED_EVENT } from "./lib/api";
import { Shell } from "./features/shell/Shell";
import { LoginPage } from "./features/auth/LoginPage";

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

  if (status !== "ready") {
    return <div className="grid h-full place-items-center bg-(--void) text-(--ice-3)">در حال بارگذاری…</div>;
  }

  return user ? <Shell /> : <LoginPage />;
}

export default App;
