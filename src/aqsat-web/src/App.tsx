import { lazy, Suspense } from "react";
import { useEffect } from "react";
import { useAuthStore } from "./app/store/authStore";
import { AUTH_CLEARED_EVENT } from "./lib/api";
import { Shell } from "./features/shell/Shell";
import { LoginPage } from "./features/auth/LoginPage";

/// Feature #8 — the anonymous public pages (owner setup, portal, /pay) are split into their own
/// chunks: a customer opening an SMS payment link downloads only that page's code, never the MDI
/// shell, the login flow, or any feature module. Reached by URL alone, so the chunk loads once
/// and the tiny fallback is all the user sees for a moment.
const OwnerSetupPage = lazy(() =>
  import("./features/auth/OwnerSetupPage").then((m) => ({ default: m.OwnerSetupPage })));
/// Feature 5 — the public self-serve agency signup form, split like the other anonymous pages.
const AgencySignupPage = lazy(() =>
  import("./features/auth/AgencySignupPage").then((m) => ({ default: m.AgencySignupPage })));
const PortalPage = lazy(() =>
  import("./features/portal/PortalPage").then((m) => ({ default: m.PortalPage })));
const InstallmentPayPage = lazy(() =>
  import("./features/portal/InstallmentPayPage").then((m) => ({ default: m.InstallmentPayPage })));

const ChunkFallback = () => (
  <div className="grid h-full place-items-center bg-(--void) text-(--ice-3)">در حال بارگذاری…</div>
);

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
    return (
      <Suspense fallback={<ChunkFallback />}>
        <OwnerSetupPage />
      </Suspense>
    );
  }

  // Public self-serve agency signup (feature 5) — linked from the login page.
  if (window.location.pathname === "/signup") {
    return (
      <Suspense fallback={<ChunkFallback />}>
        <AgencySignupPage />
      </Suspense>
    );
  }

  // The public customer portal — anonymous, the token in the URL is the credential.
  if (window.location.pathname.startsWith("/portal/")) {
    return (
      <Suspense fallback={<ChunkFallback />}>
        <PortalPage />
      </Suspense>
    );
  }

  // The public installment-payment page the SMS reminder links to — same anonymous-token model.
  if (window.location.pathname.startsWith("/pay/")) {
    return (
      <Suspense fallback={<ChunkFallback />}>
        <InstallmentPayPage />
      </Suspense>
    );
  }

  if (status !== "ready") {
    return <ChunkFallback />;
  }

  return user ? <Shell /> : <LoginPage />;
}

export default App;
