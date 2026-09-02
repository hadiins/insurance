import { useCallback, useEffect, useRef, useState } from "react";
import { useTabsStore } from "../../app/store/tabsStore";
import { api } from "../../lib/api";
import { fa } from "../../lib/persian";
import { subscribeToDataChanges } from "../../lib/dataEvents";

interface IncompleteProfileSummaryDto {
  total: number;
  withoutMobile: number;
  withoutNationalId: number;
}

interface TodaySummaryDto {
  overdueInstallments: number;
  dueRenewals: number;
  incompleteProfiles: IncompleteProfileSummaryDto;
}

const POLL_INTERVAL_MS = 60_000;

export function NotificationBell() {
  const openTab = useTabsStore((s) => s.openTab);
  const [summary, setSummary] = useState<TodaySummaryDto | null>(null);
  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);

  const reload = useCallback(() => {
    api
      .get<TodaySummaryDto>("/countdown/summary")
      .then(setSummary)
      .catch(() => setSummary(null));
  }, []);

  useEffect(() => {
    reload();
    const poll = setInterval(reload, POLL_INTERVAL_MS);
    const unsubscribe = subscribeToDataChanges(reload);
    return () => {
      clearInterval(poll);
      unsubscribe();
    };
  }, [reload]);

  useEffect(() => {
    if (!open) return;
    function onPointerDown(e: MouseEvent) {
      if (rootRef.current && !rootRef.current.contains(e.target as Node)) setOpen(false);
    }
    function onKeyDown(e: KeyboardEvent) {
      if (e.key === "Escape") setOpen(false);
    }
    document.addEventListener("mousedown", onPointerDown);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("mousedown", onPointerDown);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [open]);

  const overdue = summary?.overdueInstallments ?? 0;
  const renewals = summary?.dueRenewals ?? 0;
  const incomplete = summary?.incompleteProfiles.total ?? 0;
  const total = overdue + renewals + incomplete;

  return (
    <div ref={rootRef} className="relative flex-none">
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        title="اعلان‌ها"
        className="relative grid h-8 w-8 place-items-center rounded-[9px] border border-(--edge) text-[15px] text-(--ice-2) transition-colors hover:bg-(--hov) hover:text-(--ice)"
      >
        🔔
        {total > 0 && (
          <span className="absolute -end-1 -top-1 grid min-w-4.5 place-items-center rounded-full bg-(--ember) px-1 text-[10px] font-bold leading-4.5 text-(--on-mint)">
            {fa(total)}
          </span>
        )}
      </button>

      {open && (
        <div className="absolute end-0 top-9 z-50 w-64 rounded-[12px] border border-(--edge-2) bg-(--pane) p-1.5 shadow-lg">
          <BellRow
            label="اقساط معوق"
            count={overdue}
            tone={overdue > 0 ? "ember" : "mute"}
            onClick={() => {
              openTab({ navType: "today", page: "today", kind: "singleton", title: "امروز", pinned: true });
              setOpen(false);
            }}
          />
          <BellRow
            label="سررسید تمدید نزدیک"
            count={renewals}
            tone={renewals > 0 ? "amber" : "mute"}
            onClick={() => {
              openTab({ navType: "renewal-watches", page: "renewal-watches", kind: "singleton", title: "سررسید تمدید" });
              setOpen(false);
            }}
          />
          <BellRow
            label="پروندهٔ مشتری ناقص"
            count={incomplete}
            tone={incomplete > 0 ? "amber" : "mute"}
            onClick={() => {
              openTab({
                navType: "customer-completion",
                page: "customer-completion",
                kind: "singleton",
                title: "تکمیل پروندهٔ مشتریان",
              });
              setOpen(false);
            }}
          />
        </div>
      )}
    </div>
  );
}

function BellRow({
  label,
  count,
  tone,
  onClick,
}: {
  label: string;
  count: number;
  tone: "ember" | "amber" | "mute";
  onClick: () => void;
}) {
  const countClass =
    tone === "ember"
      ? "text-(--ember)"
      : tone === "amber"
        ? "text-(--amber)"
        : "text-(--ice-3)";
  return (
    <button
      type="button"
      onClick={onClick}
      className="flex w-full items-center justify-between rounded-[9px] px-2.5 py-2 text-right text-[12.5px] text-(--ice-2) transition-colors hover:bg-(--hov)"
    >
      <span>{label}</span>
      <b className={`text-[14px] font-extrabold ${countClass}`}>{fa(count)}</b>
    </button>
  );
}
