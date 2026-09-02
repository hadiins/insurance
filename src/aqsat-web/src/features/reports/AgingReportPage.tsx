import { useEffect, useState } from "react";
import { api, ApiError, getToken, getActiveOrgId } from "../../lib/api";
import { fa, money } from "../../lib/persian";
import { toJalaliDisplay } from "../../lib/jalali";

interface AgingReportRow {
  customerId: string;
  customerFullName: string;
  customerMobile: string | null;
  openCount: number;
  currentAmount: number;
  overdue1To30: number;
  overdue31To60: number;
  overdue60Plus: number;
  totalOpen: number;
  oldestOverdueDueDate: string | null;
}

interface AgingReportDto {
  totalCurrentAmount: number;
  totalOverdue1To30: number;
  totalOverdue31To60: number;
  totalOverdue60Plus: number;
  totalOpen: number;
  rows: AgingReportRow[];
}

/** «سنی معوقات» — every customer's open installments bucketed by days past due, worst debtors
 * first. Buckets hold the remaining balance, never the original amount. */
export function AgingReportPage() {
  const [report, setReport] = useState<AgingReportDto | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    api
      .get<AgingReportDto>("/reports/aging")
      .then(setReport)
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری گزارش"));
  }, []);

  async function exportXlsx() {
    const token = getToken();
    const orgId = getActiveOrgId();
    const headers = new Headers();
    if (token) headers.set("Authorization", `Bearer ${token}`);
    if (orgId) headers.set("X-Organization-Id", orgId);

    const response = await fetch("/api/reports/aging/export", { headers });
    if (!response.ok) {
      setError("خروجی اکسل ناموفق بود.");
      return;
    }
    const blob = await response.blob();
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `aging-${new Date().toISOString().slice(0, 10)}.xlsx`;
    link.click();
    URL.revokeObjectURL(url);
  }

  return (
    <div>
      <div className="mb-1 flex items-center justify-between">
        <h2 className="text-xl font-extrabold tracking-tight text-(--ice)">
          سنی <em className="font-extralight not-italic text-(--ice-2)">معوقات</em>
        </h2>
        {report && report.rows.length > 0 && (
          <button
            type="button"
            onClick={exportXlsx}
            className="rounded-[10px] border border-(--edge-2) bg-(--btn-bg) px-4 py-2 text-[12.5px] font-semibold text-(--ice-2) transition-colors hover:bg-(--btn-hov) hover:text-(--ice)"
          >
            خروجی اکسل
          </button>
        )}
      </div>
      <div className="mb-4.5 text-xs text-(--ice-3)">اقساط باز مشتریان به تفکیک روزهای تأخیر — بر پایهٔ ماندهٔ هر قسط</div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      {report && (
        <div className="mb-4.5 grid grid-cols-5 gap-3 text-center text-[11px]">
          <div className="rounded-2xl border border-(--edge) bg-(--pane) p-3">
            <div className="text-(--ice-3)">جاری</div>
            <div className="mt-1 text-[15px] font-extrabold tabular-nums text-(--ice)">{money(report.totalCurrentAmount)}</div>
          </div>
          <div className="rounded-2xl border border-(--edge) bg-(--pane) p-3">
            <div className="text-(--ice-3)">۱ تا ۳۰ روز</div>
            <div className="mt-1 text-[15px] font-extrabold tabular-nums text-(--amber)">{money(report.totalOverdue1To30)}</div>
          </div>
          <div className="rounded-2xl border border-(--edge) bg-(--pane) p-3">
            <div className="text-(--ice-3)">۳۱ تا ۶۰ روز</div>
            <div className="mt-1 text-[15px] font-extrabold tabular-nums text-(--ember)">{money(report.totalOverdue31To60)}</div>
          </div>
          <div className="rounded-2xl border border-(--edge) bg-(--pane) p-3">
            <div className="text-(--ice-3)">بیش از ۶۰ روز</div>
            <div className="mt-1 text-[15px] font-extrabold tabular-nums text-(--ember)">{money(report.totalOverdue60Plus)}</div>
          </div>
          <div className="rounded-2xl border border-(--mint)/30 bg-(--mint)/6 p-3">
            <div className="text-(--ice-3)">جمع بدهی باز</div>
            <div className="mt-1 text-[15px] font-extrabold tabular-nums text-(--ice)">{money(report.totalOpen)}</div>
          </div>
        </div>
      )}

      <div className="overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
        <div className="border-b border-(--edge) px-3 py-2.5 text-[12.5px] font-semibold text-(--ice-2)">
          به تفکیک مشتری
          {report && <span className="ms-2 text-[11px] font-normal text-(--ice-3)">{fa(report.rows.length)} مشتری</span>}
        </div>
        <table className="w-full border-collapse">
          <thead>
            <tr>
              {["مشتری", "موبایل", "تعداد قسط باز", "جاری", "۱ تا ۳۰ روز", "۳۱ تا ۶۰ روز", "بیش از ۶۰ روز", "جمع بدهی", "قدیمی‌ترین قسط سررسیدشده"].map((h) => (
                <th key={h} className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                  {h}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {report?.rows.map((r) => (
              <tr key={r.customerId} className="border-t border-(--edge) first:border-t-0">
                <td className="px-3 py-2.5 text-[13px] font-semibold">{r.customerFullName}</td>
                <td className="px-3 py-2.5 text-[12px] tabular-nums text-(--ice-3)" dir="ltr">{r.customerMobile ?? "—"}</td>
                <td className="px-3 py-2.5 text-[12.5px] tabular-nums text-(--ice-3)">{fa(r.openCount)}</td>
                <td className="px-3 py-2.5 text-[12.5px] font-bold tabular-nums text-(--ice-2)">{money(r.currentAmount)}</td>
                <td className="px-3 py-2.5 text-[12.5px] font-bold tabular-nums text-(--amber)">{money(r.overdue1To30)}</td>
                <td className="px-3 py-2.5 text-[12.5px] font-bold tabular-nums text-(--ember)">{money(r.overdue31To60)}</td>
                <td className="px-3 py-2.5 text-[12.5px] font-bold tabular-nums text-(--ember)">{money(r.overdue60Plus)}</td>
                <td className="px-3 py-2.5 text-[13px] font-extrabold tabular-nums">{money(r.totalOpen)}</td>
                <td className="px-3 py-2.5 text-[12px] tabular-nums text-(--ice-3)">
                  {r.oldestOverdueDueDate ? toJalaliDisplay(r.oldestOverdueDueDate) : "—"}
                </td>
              </tr>
            ))}
            {report?.rows.length === 0 && (
              <tr>
                <td colSpan={9} className="px-3 py-6 text-center text-[12.5px] text-(--ice-3)">
                  هیچ قسط بازی وجود ندارد.
                </td>
              </tr>
            )}
            {report === null && !error && (
              <tr>
                <td colSpan={9} className="px-3 py-6 text-center text-[12px] text-(--ice-3)">در حال بارگذاری…</td>
              </tr>
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
