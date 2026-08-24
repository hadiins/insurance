import { useState } from "react";
import { api, ApiError, getActiveOrgId, getToken } from "../../lib/api";
import { money } from "../../lib/persian";
import { JalaliDateField } from "../../components/JalaliDateField";

interface PnlBreakdownRow {
  groupKey: string;
  groupLabel: string;
  totalIncome: number;
  totalExpense: number;
  netProfit: number;
}

interface PnlResultDto {
  agencyCommissionIncome: number;
  serviceFeeIncome: number;
  marketerCommissionExpense: number;
  defaultWriteOffExpense: number;
  totalIncome: number;
  totalExpense: number;
  netProfit: number;
  byLine: PnlBreakdownRow[];
  byMarketer: PnlBreakdownRow[];
  byMonth: PnlBreakdownRow[];
}

const TODAY = new Date().toISOString().slice(0, 10);
const MONTH_AGO = new Date(Date.now() - 30 * 86_400_000).toISOString().slice(0, 10);

export function PnlPage() {
  const [from, setFrom] = useState(MONTH_AGO);
  const [to, setTo] = useState(TODAY);
  const [basis, setBasis] = useState<"Accrual" | "Cash">("Accrual");
  const [writeOffDays, setWriteOffDays] = useState("");
  const [result, setResult] = useState<PnlResultDto | null>(null);
  const [compareBasis, setCompareBasis] = useState<PnlResultDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  function buildQuery(basisValue: "Accrual" | "Cash") {
    const params = new URLSearchParams({ from, to, basis: basisValue });
    if (writeOffDays) params.set("writeOffThresholdDays", writeOffDays);
    return params.toString();
  }

  async function run() {
    setBusy(true);
    setError(null);
    try {
      const [primary, other] = await Promise.all([
        api.get<PnlResultDto>(`/reports/pnl?${buildQuery(basis)}`),
        api.get<PnlResultDto>(`/reports/pnl?${buildQuery(basis === "Accrual" ? "Cash" : "Accrual")}`),
      ]);
      setResult(primary);
      setCompareBasis(other);
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

    const response = await fetch(`/api/reports/pnl/export?${buildQuery(basis)}`, { headers });
    if (!response.ok) {
      setError("خروجی اکسل ناموفق بود.");
      return;
    }
    const blob = await response.blob();
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `pnl-${from}-${to}.xlsx`;
    link.click();
    URL.revokeObjectURL(url);
  }

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">
        سود <em className="font-extralight not-italic text-(--ice-2)">و زیان</em>
      </h2>
      <div className="mb-4.5 text-xs text-(--ice-3)">درآمد کارمزد شرکت بیمه + کارمزد خدمات، منهای پورسانت بازاریاب و سوخت نکول</div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      <div className="mb-4.5 rounded-2xl border border-(--edge) bg-(--pane) p-5">
        <div className="mb-3.5 grid grid-cols-4 gap-3">
          <div>
            <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">از تاریخ</label>
            <JalaliDateField value={from} onChange={setFrom} className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13px] text-(--ice)" />
          </div>
          <div>
            <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">تا تاریخ</label>
            <JalaliDateField value={to} onChange={setTo} className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13px] text-(--ice)" />
          </div>
          <div>
            <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">مبنا</label>
            <select
              value={basis}
              onChange={(e) => setBasis(e.target.value as "Accrual" | "Cash")}
              className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13px] text-(--ice)"
            >
              <option value="Accrual">تعهدی (صدور)</option>
              <option value="Cash">نقدی (وصول)</option>
            </select>
          </div>
          <div>
            <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">آستانهٔ سوخت نکول (روز)</label>
            <input
              value={writeOffDays}
              onChange={(e) => setWriteOffDays(e.target.value)}
              placeholder="پیش‌فرض نمایندگی"
              className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13px] text-(--ice)"
            />
          </div>
        </div>
        <div className="flex gap-2">
          <button
            type="button"
            onClick={run}
            disabled={busy}
            className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) shadow-[var(--gl-mint)] transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
          >
            {busy ? "در حال محاسبه…" : "دریافت گزارش"}
          </button>
          {result && (
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

      {result && compareBasis && (
        <>
          <div className="mb-4.5 grid grid-cols-2 gap-4">
            <PnlSummaryCard title={basis === "Accrual" ? "تعهدی" : "نقدی"} result={result} highlight />
            <PnlSummaryCard title={basis === "Accrual" ? "نقدی" : "تعهدی"} result={compareBasis} />
          </div>

          <div className="grid grid-cols-3 gap-4">
            <BreakdownTable title="به‌تفکیک رشته" rows={result.byLine} />
            <BreakdownTable title="به‌تفکیک بازاریاب" rows={result.byMarketer} />
            <BreakdownTable title="به‌تفکیک ماه" rows={result.byMonth} />
          </div>
        </>
      )}
    </div>
  );
}

function PnlSummaryCard({ title, result, highlight }: { title: string; result: PnlResultDto; highlight?: boolean }) {
  const netColor = result.netProfit >= 0 ? "text-(--mint)" : "text-(--ember)";
  return (
    <div className={`rounded-2xl border p-5 ${highlight ? "border-(--mint)/30 bg-(--mint)/6" : "border-(--edge) bg-(--pane)"}`}>
      <div className="mb-3 text-[13px] font-bold text-(--ice)">{title}</div>
      <div className="mb-3 grid grid-cols-2 gap-2 text-[12px]">
        <Line label="کارمزد از بیمه‌گر" value={result.agencyCommissionIncome} />
        <Line label="کارمزد خدمات" value={result.serviceFeeIncome} />
        <Line label="پورسانت بازاریاب" value={-result.marketerCommissionExpense} />
        <Line label="سوخت نکول" value={-result.defaultWriteOffExpense} />
      </div>
      <div className="border-t border-(--edge-2) pt-2.5">
        <div className="text-[10px] tracking-wider text-(--ice-3)">سود خالص</div>
        <div className={`text-[22px] font-extrabold ${netColor}`}>{money(result.netProfit)}</div>
      </div>
    </div>
  );
}

function Line({ label, value }: { label: string; value: number }) {
  return (
    <div className="flex items-center justify-between">
      <span className="text-(--ice-3)">{label}</span>
      <span className={value < 0 ? "text-(--ember)" : "text-(--ice)"}>{money(value)}</span>
    </div>
  );
}

function BreakdownTable({ title, rows }: { title: string; rows: PnlBreakdownRow[] }) {
  return (
    <div className="overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
      <div className="border-b border-(--edge) px-3.5 py-2.5 text-[12px] font-semibold text-(--ice-2)">{title}</div>
      {rows.length === 0 ? (
        <div className="p-4 text-center text-[11.5px] text-(--ice-3)">داده‌ای نیست</div>
      ) : (
        rows.map((r) => (
          <div key={r.groupKey} className="flex items-center justify-between border-t border-(--edge) px-3.5 py-2 text-[11.5px] first:border-t-0">
            <span className="text-(--ice-2)">{r.groupLabel}</span>
            <span className={r.netProfit >= 0 ? "font-bold text-(--mint)" : "font-bold text-(--ember)"}>{money(r.netProfit)}</span>
          </div>
        ))
      )}
    </div>
  );
}
