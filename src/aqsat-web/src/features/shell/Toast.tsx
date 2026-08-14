import { useEffect } from "react";
import { useTabsStore } from "../../app/store/tabsStore";

export function Toast() {
  const message = useTabsStore((s) => s.toastMessage);
  const dismissToast = useTabsStore((s) => s.dismissToast);

  useEffect(() => {
    if (!message) return;
    const t = setTimeout(dismissToast, 2200);
    return () => clearTimeout(t);
  }, [message, dismissToast]);

  return (
    <div
      className={`fixed bottom-5.5 start-5.5 z-[70] flex items-center gap-2.5 rounded-xl border border-(--edge-2) bg-(--slate-2) px-4.5 py-2.5 text-[12.5px] text-(--ice) shadow-[var(--sh)] transition-transform duration-300 ${
        message ? "translate-y-0" : "translate-y-[160%]"
      }`}
    >
      <span className="h-1.5 w-1.5 flex-none rounded-full bg-(--mint) shadow-[var(--gl-mint)]" />
      <span>{message}</span>
    </div>
  );
}
