import { useEffect, useState } from "react";
import { api, ApiError, getToken, getActiveOrgId } from "../../lib/api";
import { fa, money } from "../../lib/persian";
import { toJalaliDisplay } from "../../lib/jalali";
import { MetricCard } from "../../components/MetricCard";
import { EmptyState } from "../../components/EmptyState";
import { Td, Th, Tr } from "../../components/Table";

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
      <div className="mb-4.5 text-[12.5px] text-(--ice-3)">اقساط باز مشتریان به تفکیک روزهای تأخیر — بر پایهٔ ماندهٔ هر قسط</div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      {report && (
        <div className="mb-4.5 grid grid-cols-5 gap-3">
          <MetricCard icon="🟢" label="جاری" value={report.totalCurrentAmount} />
          <MetricCard icon="🕐" label="۱ تا ۳۰ روز" value={report.totalOverdue1To30} tone="amber" />
          <MetricCard icon="⏰" label="۳۱ تا ۶۰ روز" value={report.totalOverdue31To60} tone="ember" />
          <MetricCard icon="🚨" label="بیش از ۶۰ روز" value={report.totalOverdue60Plus} tone="ember" />
          <MetricCard icon="💼" label="جمع بدهی باز" value={report.totalOpen} tone="mint" />
        </div>
      )}

      {report !== null && report.rows.length === 0 ? (
        <EmptyState
          icon="✅"
          title="هیچ قسط بازی وجود ندارد"
          description="در حال حاضر هیچ مشتری‌ای قسط باز ندارد؛ همهٔ اقساط تسویه شده‌اند."
        />
      ) : (
        <div className="overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
          <div className="border-b border-(--edge) px-3 py-2.5 text-[12.5px] font-semibold text-(--ice-2)">
            به تفکیک مشتری
            {report && <span className="ms-2 text-[11.5px] font-normal text-(--ice-3)">{fa(report.rows.length)} مشتری</span>}
          </div>
          <table className="w-full border-collapse">
            <thead>
              <tr>
                {["مشتری", "موبایل", "تعداد قسط باز", "جاری", "۱ تا ۳۰ روز", "۳۱ تا ۶۰ روز", "بیش از ۶۰ روز", "جمع بدهی", "قدیمی‌ترین قسط سررسیدشده"].map((h) => (
                  <Th key={h}>{h}</Th>
                ))}
              </tr>
            </thead>
            <tbody>
              {report?.rows.map((r) => (
                <Tr key={r.customerId}>
                  <Td className="py-2.5 font-semibold">{r.customerFullName}</Td>
                  <Td className="py-2.5 !text-[12.5px] tabular-nums text-(--ice-3)" ltr>{r.customerMobile ?? "—"}</Td>
                  <Td className="py-2.5 !text-[12.5px] tabular-nums text-(--ice-3)">{fa(r.openCount)}</Td>
                  <Td className="py-2.5 !text-[12.5px] font-bold tabular-nums text-(--ice-2)">{money(r.currentAmount)}</Td>
                  <Td className="py-2.5 !text-[12.5px] font-bold tabular-nums text-(--amber)">{money(r.overdue1To30)}</Td>
                  <Td className="py-2.5 !text-[12.5px] font-bold tabular-nums text-(--ember)">{money(r.overdue31To60)}</Td>
                  <Td className="py-2.5 !text-[12.5px] font-bold tabular-nums text-(--ember)">{money(r.overdue60Plus)}</Td>
                  <Td className="py-2.5 font-extrabold tabular-nums">{money(r.totalOpen)}</Td>
                  <Td className="py-2.5 !text-[12.5px] tabular-nums text-(--ice-3)">
                    {r.oldestOverdueDueDate ? toJalaliDisplay(r.oldestOverdueDueDate) : "—"}
                  </Td>
                </Tr>
              ))}
              {report === null && !error && (
                <tr>
                  <td colSpan={9} className="px-3 py-6 text-center text-[12.5px] text-(--ice-3)">در حال بارگذاری…</td>
                </tr>
              )}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
}
