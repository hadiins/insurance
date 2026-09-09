import { useState } from "react";
import { useTabsStore } from "../../app/store/tabsStore";
import { ApiError } from "../../lib/api";
import { fa } from "../../lib/persian";
import { toJalaliDateTimeDisplay } from "../../lib/jalali";
import { EmptyState } from "../../components/EmptyState";
import { useMarkWarningRead, useRiskWarnings } from "./riskApi";

/** «هشدارها» (docs Phase 2A §19) — the early-warning feed: level escalation, score drops,
 * near-limit debt, rapid debt growth, new bounced cheques. Messages were composed at write time
 * (rule 31); this page only lists and marks them read. */
export function RiskWarningsPage() {
  const openTab = useTabsStore((s) => s.openTab);
  const [unreadOnly, setUnreadOnly] = useState(false);
  const warnings = useRiskWarnings(unreadOnly);
  const markRead = useMarkWarningRead();

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">هشدارهای ریسک</h2>
      <div className="mb-4.5 text-[12.5px] text-(--ice-3)">
        تغییرات مهم وضعیت اعتباری مشتریان — افزایش سطح ریسک، افت امتیاز، رشد بدهی و چک برگشتی جدید
      </div>

      <div className="mb-3 flex flex-wrap items-center gap-2">
        <button
          type="button"
          onClick={() => setUnreadOnly(false)}
          className={`rounded-full px-3 py-1 text-[11.5px] font-semibold transition-colors ${
            !unreadOnly ? "bg-(--mint) text-(--on-mint)" : "border border-(--edge-2) text-(--ice-3) hover:bg-(--hov)"
          }`}
        >
          همه
        </button>
        <button
          type="button"
          onClick={() => setUnreadOnly(true)}
          className={`rounded-full px-3 py-1 text-[11.5px] font-semibold transition-colors ${
            unreadOnly ? "bg-(--mint) text-(--on-mint)" : "border border-(--edge-2) text-(--ice-3) hover:bg-(--hov)"
          }`}
        >
          خوانده‌نشده
        </button>
      </div>

      {warnings.isPending && <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>}

      {warnings.isError && (
        <div className="rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {warnings.error instanceof ApiError ? warnings.error.message : "خطا در بارگذاری هشدارها"}
          <button type="button" onClick={() => void warnings.refetch()} className="ms-2 underline">
            تلاش مجدد
          </button>
        </div>
      )}

      {warnings.data && (
        <>
          <div className="mb-2 text-[11.5px] text-(--ice-3)">{fa(warnings.data.length)} هشدار</div>
          {warnings.data.length === 0 ? (
            <EmptyState
              icon="🔔"
              title={unreadOnly ? "هشدار خوانده‌نشده‌ای نیست." : "هشداری ثبت نشده."}
              description="هشدارها هنگام ارزیابی مجدد مشتریان ساخته می‌شوند — هر تغییر مهم در وضعیت اعتباری اینجا گزارش می‌شود."
            />
          ) : (
            <div className="overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
              {warnings.data.map((w) => (
                <div
                  key={w.id}
                  className={`flex items-start gap-3 border-t border-(--edge) px-4 py-3 text-[12.5px] first:border-t-0 ${
                    w.isRead ? "opacity-65" : ""
                  }`}
                >
                  <span className="mt-0.5 text-[14px]">⚠</span>
                  <div className="min-w-0 flex-1">
                    <div className="flex flex-wrap items-center gap-2">
                      <span className="rounded-full border border-(--amber)/40 bg-(--amber)/10 px-2 py-0.5 text-[11px] font-semibold text-(--amber)">
                        {w.typeFa}
                      </span>
                      <button
                        type="button"
                        onClick={() =>
                          openTab({
                            navType: "customer-file",
                            page: "customer-file",
                            kind: "multi-record",
                            recordId: w.customerId,
                            title: w.customerName,
                            payload: { customerId: w.customerId },
                          })
                        }
                        className="font-semibold text-(--ice-2) underline underline-offset-2 hover:text-(--ice)"
                      >
                        {w.customerName}
                      </button>
                      <span className="text-[11px] text-(--ice-3)">{toJalaliDateTimeDisplay(w.createdAt)}</span>
                    </div>
                    <div className="mt-1 leading-relaxed text-(--ice-2)">{w.message}</div>
                  </div>
                  {!w.isRead && (
                    <button
                      type="button"
                      onClick={() => markRead.mutate(w.id)}
                      disabled={markRead.isPending}
                      className="rounded-[10px] border border-(--edge-2) px-2.5 py-1 text-[11px] font-semibold text-(--ice-3) transition-colors hover:bg-(--hov) hover:text-(--ice) disabled:opacity-50"
                    >
                      خوانده شد
                    </button>
                  )}
                </div>
              ))}
            </div>
          )}
        </>
      )}
    </div>
  );
}
