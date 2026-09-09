import { useState } from "react";
import { api, ApiError, getActiveOrgId, getToken } from "../../lib/api";
import { fa, money } from "../../lib/persian";
import { toJalaliDisplay } from "../../lib/jalali";
import { JalaliDateField } from "../../components/JalaliDateField";
import { EmptyState } from "../../components/EmptyState";
import { Table, Td, Th, Tr } from "../../components/Table";

interface CollectionsReportRow {
  policyNumber: string;
  customerFullName: string;
  insuranceLineNameFa: string;
  seqNo: number;
  dueDate: string;
  settlementDeadline: string;
  amount: number;
  paidAmount: number;
  balance: number;
  status: string;
}

interface CollectionsReportPageDto {
  totalCount: number;
  rows: CollectionsReportRow[];
}

interface CollectionsSummaryDto {
  totalCount: number;
  totalDue: number;
  totalCollected: number;
  settledOnTimeCount: number;
  settledLateCount: number;
  openCount: number;
  writtenOffCount: number;
  onTimeRate: number;
  defaultRate: number;
}

const STATUS_LABEL: Record<string, string> = {
  Unpaid: "پرداخت‌نشده",
  Partial: "پرداخت جزئی",
  Settled: "تسویه‌شده",
};

const PAGE_SIZE = 25;
const TODAY = new Date().toISOString().slice(0, 10);
const MONTH_AGO = new Date(Date.now() - 30 * 86_400_000).toISOString().slice(0, 10);

export function CollectionsReportPage() {
  const [from, setFrom] = useState(MONTH_AGO);
  const [to, setTo] = useState(TODAY);
  const [page, setPage] = useState(1);
  const [summary, setSummary] = useState<CollectionsSummaryDto | null>(null);
  const [pageData, setPageData] = useState<CollectionsReportPageDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function run(targetPage = 1) {
    setBusy(true);
    setError(null);
    try {
      const [summaryResult, pageResult] = await Promise.all([
        api.get<CollectionsSummaryDto>(`/reports/collections/summary?from=${from}&to=${to}`),
        api.get<CollectionsReportPageDto>(`/reports/collections?from=${from}&to=${to}&page=${targetPage}&pageSize=${PAGE_SIZE}`),
      ]);
      setSummary(summaryResult);
      setPageData(pageResult);
      setPage(targetPage);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "دریافت گزارش ناموفق بود.");
    } finally {
      setBusy(false);
    }
  }

  async function exportXlsx() {
    const token = getToken();
    const orgId = getActiveOrgId();
    const headers = new Headers();
    if (token) headers.set("Authorization", `Bearer ${token}`);
    if (orgId) headers.set("X-Organization-Id", orgId);

    const response = await fetch(`/api/reports/collections/export?from=${from}&to=${to}`, { headers });
    if (!response.ok) {
      setError("خروجی اکسل ناموفق بود.");
      return;
    }
    const blob = await response.blob();
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `collections-${from}-${to}.xlsx`;
    link.click();
    URL.revokeObjectURL(url);
  }

  const totalPages = pageData ? Math.max(1, Math.ceil(pageData.totalCount / PAGE_SIZE)) : 1;

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">
        وصولی‌های <em className="font-extralight not-italic text-(--ice-2)">دوره</em>
      </h2>
      <div className="mb-4.5 text-[12.5px] text-(--ice-3)">وصولی، نرخ به‌موقع‌بودن و تحلیل نکول</div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      <div className="mb-4.5 rounded-2xl border border-(--edge) bg-(--pane) p-5">
        <div className="mb-3.5 grid grid-cols-2 gap-3">
          <div>
            <label className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">از تاریخ</label>
            <JalaliDateField value={from} onChange={setFrom} className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice)" />
          </div>
          <div>
            <label className="mb-1.5 block text-[11.5px] tracking-wider text-(--ice-3)">تا تاریخ</label>
            <JalaliDateField value={to} onChange={setTo} className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice)" />
          </div>
        </div>
        <div className="flex gap-2">
          <button
            type="button"
            onClick={() => run(1)}
            disabled={busy}
            className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) shadow-[var(--gl-mint)] transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
          >
            {busy ? "در حال محاسبه…" : "دریافت گزارش"}
          </button>
          {pageData && (
            <button
              type="button"
              onClick={exportXlsx}
              className="rounded-[10px] border border-(--edge-2) bg-(--btn-bg) px-4 py-2 text-[12.5px] font-semibold text-(--ice-2) transition-colors hover:bg-(--btn-hov) hover:text-(--ice)"
            >
              خروجی اکسل
            </button>
          )}
        </div>
      </div>

      {summary && (
        <div className="mb-4.5 grid grid-cols-6 gap-3">
          <Fig label="تعداد اقساط" value={fa(summary.totalCount)} />
          <Fig label="جمع سررسید" value={money(summary.totalDue)} />
          <Fig label="وصول‌شده" value={money(summary.totalCollected)} tone="mint" />
          <Fig label="نرخ وصول به‌موقع" value={`${fa(summary.onTimeRate)}٪`} tone="mint" />
          <Fig label="نرخ نکول" value={`${fa(summary.defaultRate)}٪`} tone="ember" />
          <Fig label="معوق سوخت‌شده" value={fa(summary.writtenOffCount)} tone="ember" />
        </div>
      )}

      {pageData && (
        <>
          <div className="mb-2 text-[11.5px] text-(--ice-3)">{fa(pageData.totalCount)} قسط</div>
          {pageData.rows.length === 0 ? (
            <EmptyState
              icon="🗓"
              title="قسطی در این بازه نیست"
              description="در بازهٔ انتخابی قسط سررسیدشده‌ای وجود ندارد؛ بازهٔ زمانی را تغییر دهید."
            />
          ) : (
            <Table>
              <thead>
                <tr>
                  {["بیمه‌نامه", "بیمه‌گذار", "رشته", "قسط", "سررسید", "مانده", "وضعیت"].map((h) => (
                    <Th key={h}>{h}</Th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {pageData.rows.map((r, idx) => (
                  <Tr key={idx}>
                    <Td className="py-2.5 font-semibold">{fa(r.policyNumber)}</Td>
                    <Td className="py-2.5 text-(--ice-3)">{r.customerFullName}</Td>
                    <Td className="py-2.5 text-(--ice-3)">{r.insuranceLineNameFa}</Td>
                    <Td className="py-2.5">{fa(r.seqNo)}</Td>
                    <Td className="py-2.5">{toJalaliDisplay(r.dueDate)}</Td>
                    <Td className="py-2.5 font-bold">{money(r.balance)}</Td>
                    <Td className="py-2.5 text-(--ice-3)">{STATUS_LABEL[r.status] ?? r.status}</Td>
                  </Tr>
                ))}
              </tbody>
            </Table>
          )}
          {totalPages > 1 && (
            <div className="mt-3 flex items-center justify-center gap-2">
              <button
                type="button"
                disabled={page <= 1}
                onClick={() => run(page - 1)}
                className="rounded-[8px] border border-(--edge-2) px-3 py-1 text-[11.5px] text-(--ice-3) transition-colors hover:bg-(--hov) disabled:cursor-not-allowed disabled:opacity-40"
              >
                قبلی
              </button>
              <span className="text-[11.5px] text-(--ice-3)">
                {fa(page)} از {fa(totalPages)}
              </span>
              <button
                type="button"
                disabled={page >= totalPages}
                onClick={() => run(page + 1)}
                className="rounded-[8px] border border-(--edge-2) px-3 py-1 text-[11.5px] text-(--ice-3) transition-colors hover:bg-(--hov) disabled:cursor-not-allowed disabled:opacity-40"
              >
                بعدی
              </button>
            </div>
          )}
        </>
      )}
    </div>
  );
}

function Fig({ label, value, tone }: { label: string; value: string; tone?: "mint" | "ember" }) {
  const color = tone === "mint" ? "text-(--mint)" : tone === "ember" ? "text-(--ember)" : "text-(--ice)";
  return (
    <div className="rounded-[14px] border border-(--edge) bg-(--pane) p-3.5">
      <div className="mb-1 text-[10.5px] tracking-[0.16em] text-(--ice-3)">{label}</div>
      <div className={`text-[20px] font-extrabold tracking-tight ${color}`}>{value}</div>
    </div>
  );
}
