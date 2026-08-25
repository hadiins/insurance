import { useState } from "react";
import { api, ApiError } from "../../lib/api";
import { fa, money } from "../../lib/persian";
import { toJalaliDisplay } from "../../lib/jalali";
import { JalaliDateField } from "../../components/JalaliDateField";

interface ReceiptRow {
  paymentId: string;
  paidOn: string;
  policyNumber: string;
  customerFullName: string;
  amount: number;
  methodType: string;
  referenceNo: string | null;
  cashBoxName: string | null;
  bankAccountLabel: string | null;
  chequeNumber: string | null;
  chequeStatus: string | null;
}

interface ReceiptsByMethodRow {
  methodType: string;
  count: number;
  amount: number;
}

interface ReceiptsByChequeStatusRow {
  status: string;
  count: number;
  amount: number;
}

interface ReceiptsReportDto {
  totalAmount: number;
  count: number;
  byMethod: ReceiptsByMethodRow[];
  byChequeStatus: ReceiptsByChequeStatusRow[];
  rows: ReceiptRow[];
}

const METHOD_LABEL: Record<string, string> = {
  Cash: "نقدی",
  BankTransfer: "واریز بانکی",
  Cheque: "چک",
  PosDirect: "پوز مستقیم بیمه‌گر",
};
const CHEQUE_STATUS_LABEL: Record<string, string> = { Held: "نزد صندوق", AtBank: "نزد بانک", Cleared: "وصول‌شده", Bounced: "برگشتی" };

const TODAY = new Date().toISOString().slice(0, 10);
const MONTH_AGO = new Date(Date.now() - 30 * 86_400_000).toISOString().slice(0, 10);

/** «گزارش دریافتی‌ها» — Stage 6/7: every real receipt in a date range, split by صندوق/بانک/چک
 * with a cheque-status breakdown. */
export function ReceiptsReportPage() {
  const [from, setFrom] = useState(MONTH_AGO);
  const [to, setTo] = useState(TODAY);
  const [report, setReport] = useState<ReceiptsReportDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busy, setBusy] = useState(false);

  async function run() {
    setBusy(true);
    setError(null);
    try {
      const result = await api.get<ReceiptsReportDto>(`/reports/receipts?from=${from}&to=${to}`);
      setReport(result);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "دریافت گزارش ناموفق بود.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">
        گزارش <em className="font-extralight not-italic text-(--ice-2)">دریافتی‌ها</em>
      </h2>
      <div className="mb-4.5 text-xs text-(--ice-3)">دریافتی‌های صندوق، بانک و چک در بازهٔ زمانی</div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      <div className="mb-4.5 rounded-2xl border border-(--edge) bg-(--pane) p-5">
        <div className="mb-3.5 grid grid-cols-2 gap-3">
          <div>
            <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">از تاریخ</label>
            <JalaliDateField value={from} onChange={setFrom} className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13px] text-(--ice)" />
          </div>
          <div>
            <label className="mb-1.5 block text-[11px] tracking-wider text-(--ice-3)">تا تاریخ</label>
            <JalaliDateField value={to} onChange={setTo} className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13px] text-(--ice)" />
          </div>
        </div>
        <button
          type="button"
          onClick={run}
          disabled={busy}
          className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) shadow-[var(--gl-mint)] transition-colors hover:brightness-105 disabled:cursor-not-allowed disabled:opacity-50"
        >
          {busy ? "در حال محاسبه…" : "دریافت گزارش"}
        </button>
      </div>

      {report && (
        <>
          <div className="mb-4.5 grid grid-cols-2 gap-3">
            <div className="rounded-[14px] border border-(--edge) bg-(--pane) p-3.5">
              <div className="mb-1 text-[10px] tracking-[0.16em] text-(--ice-3)">جمع دریافتی</div>
              <div className="text-[17px] font-extrabold tracking-tight text-(--mint)">{money(report.totalAmount)}</div>
            </div>
            <div className="rounded-[14px] border border-(--edge) bg-(--pane) p-3.5">
              <div className="mb-1 text-[10px] tracking-[0.16em] text-(--ice-3)">تعداد رسید</div>
              <div className="text-[17px] font-extrabold tracking-tight text-(--ice)">{fa(report.count)}</div>
            </div>
          </div>

          <div className="mb-4.5 grid grid-cols-2 gap-4">
            <div className="overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
              <div className="border-b border-(--edge) px-3 py-2.5 text-[12.5px] font-semibold text-(--ice-2)">به‌تفکیک روش دریافت</div>
              <table className="w-full border-collapse">
                <tbody>
                  {report.byMethod.map((m) => (
                    <tr key={m.methodType} className="border-t border-(--edge) first:border-t-0">
                      <td className="px-3 py-2.5 text-[13px] text-(--ice-3)">{METHOD_LABEL[m.methodType] ?? m.methodType}</td>
                      <td className="px-3 py-2.5 text-[12px] text-(--ice-3)">{fa(m.count)} رسید</td>
                      <td className="px-3 py-2.5 text-end text-[13px] font-bold text-(--ice)">{money(m.amount)}</td>
                    </tr>
                  ))}
                  {report.byMethod.length === 0 && (
                    <tr>
                      <td colSpan={3} className="px-3 py-6 text-center text-[12.5px] text-(--ice-3)">
                        دریافتی‌ای در این بازه نیست.
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>

            <div className="overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
              <div className="border-b border-(--edge) px-3 py-2.5 text-[12.5px] font-semibold text-(--ice-2)">به‌تفکیک وضعیت چک</div>
              <table className="w-full border-collapse">
                <tbody>
                  {report.byChequeStatus.map((c) => (
                    <tr key={c.status} className="border-t border-(--edge) first:border-t-0">
                      <td className="px-3 py-2.5 text-[13px] text-(--ice-3)">{CHEQUE_STATUS_LABEL[c.status] ?? c.status}</td>
                      <td className="px-3 py-2.5 text-[12px] text-(--ice-3)">{fa(c.count)} چک</td>
                      <td className="px-3 py-2.5 text-end text-[13px] font-bold text-(--ice)">{money(c.amount)}</td>
                    </tr>
                  ))}
                  {report.byChequeStatus.length === 0 && (
                    <tr>
                      <td colSpan={3} className="px-3 py-6 text-center text-[12.5px] text-(--ice-3)">
                        چکی در این بازه نیست.
                      </td>
                    </tr>
                  )}
                </tbody>
              </table>
            </div>
          </div>

          <div className="overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
            <table className="w-full border-collapse">
              <thead>
                <tr>
                  {["تاریخ", "بیمه‌نامه", "بیمه‌گذار", "مبلغ", "روش", "محل دریافت", "مرجع/چک"].map((h) => (
                    <th key={h} className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                      {h}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {report.rows.map((r) => (
                  <tr key={r.paymentId} className="border-t border-(--edge) first:border-t-0">
                    <td className="px-3 py-2.5 text-[13px] tabular-nums">{toJalaliDisplay(r.paidOn)}</td>
                    <td className="px-3 py-2.5 text-[13px] font-semibold">{fa(r.policyNumber)}</td>
                    <td className="px-3 py-2.5 text-[13px] text-(--ice-3)">{r.customerFullName}</td>
                    <td className="px-3 py-2.5 text-[13px] font-bold">{money(r.amount)}</td>
                    <td className="px-3 py-2.5 text-[13px] text-(--ice-3)">{METHOD_LABEL[r.methodType] ?? r.methodType}</td>
                    <td className="px-3 py-2.5 text-[12px] text-(--ice-3)">
                      {r.cashBoxName ?? r.bankAccountLabel ?? (r.methodType === "PosDirect" ? "حساب بیمه‌گر" : "—")}
                    </td>
                    <td className="px-3 py-2.5 text-[12px] text-(--ice-3)">
                      {r.chequeNumber
                        ? `${r.chequeNumber} (${CHEQUE_STATUS_LABEL[r.chequeStatus ?? ""] ?? r.chequeStatus})`
                        : (r.referenceNo ?? "—")}
                    </td>
                  </tr>
                ))}
                {report.rows.length === 0 && (
                  <tr>
                    <td colSpan={7} className="px-3 py-6 text-center text-[12.5px] text-(--ice-3)">
                      دریافتی‌ای در این بازه نیست.
                    </td>
                  </tr>
                )}
              </tbody>
            </table>
          </div>
        </>
      )}
    </div>
  );
}
