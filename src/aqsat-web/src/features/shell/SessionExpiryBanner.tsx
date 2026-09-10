import { useEffect, useState } from "react";
import { useAuthStore } from "../../app/store/authStore";
import { fa } from "../../lib/persian";
import { beginRelogin, msUntilExpiry, SESSION_WARNING_MS } from "../../lib/sessionExpiry";

/// B12 — mounted once in Shell next to MaintenanceBanner. The 8-hour JWT used to die silently:
/// the first request after expiry threw, and the whole workspace was wiped on the spot. This
/// warns 10 minutes ahead (the timestamp comes from the login response's expiresInMinutes) and
/// offers a voluntary re-login that keeps the tabs and drafts for the same user's return.
export function SessionExpiryBanner() {
  const user = useAuthStore((s) => s.user);
  const [remainingMs, setRemainingMs] = useState<number | null>(null);
  const [dismissed, setDismissed] = useState(false);

  useEffect(() => {
    const tick = () => setRemainingMs(msUntilExpiry());
    tick();
    const timer = window.setInterval(tick, 30_000);
    return () => window.clearInterval(timer);
  }, []);

  if (!user || dismissed || remainingMs === null || remainingMs <= 0 || remainingMs > SESSION_WARNING_MS) {
    return null;
  }

  return (
    <div className="flex flex-none items-center justify-between gap-3 bg-(--amber) px-4 py-2 text-[12.5px] font-semibold text-(--void)">
      <span>
        نشست شما در کمتر از {fa(Math.ceil(remainingMs / 60_000))} دقیقه منقضی می‌شود — برای ادامهٔ کار بدون از
        دست رفتن پیش‌نویس‌ها، اکنون دوباره وارد شوید.
      </span>
      <div className="flex flex-none items-center gap-2">
        <button
          type="button"
          onClick={beginRelogin}
          className="rounded-md bg-(--void) px-2.5 py-1 text-(--ice) hover:opacity-80"
        >
          ورود مجدد
        </button>
        <button type="button" onClick={() => setDismissed(true)} className="hover:opacity-70" aria-label="بستن">
          ✕
        </button>
      </div>
    </div>
  );
}
