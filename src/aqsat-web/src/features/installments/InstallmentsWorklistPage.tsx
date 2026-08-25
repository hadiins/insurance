import { useCallback, useEffect, useState } from "react";
import { useTabsStore } from "../../app/store/tabsStore";
import { useTabKey } from "../shell/TabContext";
import { api, ApiError } from "../../lib/api";
import { fa, money } from "../../lib/persian";
import { toJalaliDisplay } from "../../lib/jalali";
import { RecordPaymentDialog } from "../today/RecordPaymentDialog";

interface WorklistFilterPayload {
  overdueOnly?: boolean;
  status?: string;
}

interface InstallmentWorklistRowDto {
  installmentId: string;
  policyId: string;
  policyNumber: string;
  customerFullName: string;
  seqNo: number;
  dueDate: string;
  settlementDeadline: string;
  amount: number;
  paidAmount: number;
  balance: number;
  status: string;
  urgency: "Overdue" | "Critical" | "Warning" | "Upcoming" | "Future";
}

const URGENCY_LABEL: Record<InstallmentWorklistRowDto["urgency"], string> = {
  Overdue: "معوق",
  Critical: "بحرانی",
  Warning: "هشدار",
  Upcoming: "سررسید نزدیک",
  Future: "آینده",
};

const STATUS_OPTIONS: { value: string; label: string }[] = [
  { value: "", label: "همهٔ وضعیت‌ها" },
  { value: "Unpaid", label: "پرداخت‌نشده" },
  { value: "Partial", label: "پرداخت جزئی (تسویه‌های جزئی)" },
  { value: "Settled", label: "تسویه‌شده" },
];

export function InstallmentsWorklistPage() {
  const tabKey = useTabKey();
  const tab = useTabsStore((s) => s.tabs.find((t) => t.key === tabKey));
  const openTab = useTabsStore((s) => s.openTab);
  const initial = (tab?.payload as WorklistFilterPayload | undefined) ?? {};

  const [rows, setRows] = useState<InstallmentWorklistRowDto[] | null>(null);
  const [payingRow, setPayingRow] = useState<InstallmentWorklistRowDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [overdueOnly, setOverdueOnly] = useState(initial.overdueOnly ?? false);
  const [status, setStatus] = useState(initial.status ?? "");

  const reload = useCallback(() => {
    const params = new URLSearchParams();
    if (overdueOnly) params.set("overdueOnly", "true");
    if (status) params.set("status", status);
    api
      .get<InstallmentWorklistRowDto[]>(`/installments?${params.toString()}`)
      .then((data) => {
        setRows(data);
        setError(null);
      })
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری فهرست"));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [overdueOnly, status]);

  useEffect(() => {
    reload();
  }, [reload]);

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">فهرست اقساط</h2>
      <div className="mb-4.5 text-xs text-(--ice-3)">بدون محدودیت بازهٔ زمانی — همهٔ ردیف‌های واجد شرایط</div>

      <div className="mb-4.5 flex flex-wrap items-center gap-2">
        <button
          type="button"
          onClick={() => setOverdueOnly((v) => !v)}
          className={`rounded-[10px] border px-3 py-1.5 text-[12px] font-semibold transition-colors ${
            overdueOnly ? "border-(--mint) bg-(--mint) text-(--on-mint)" : "border-(--edge-2) bg-(--btn-bg) text-(--ice-2) hover:bg-(--btn-hov)"
          }`}
        >
          فقط معوق
        </button>
        <select
          value={status}
          onChange={(e) => setStatus(e.target.value)}
          className="rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-1.5 text-[12px] text-(--ice) outline-none focus:border-(--mint)"
        >
          {STATUS_OPTIONS.map((o) => (
            <option key={o.value} value={o.value}>
              {o.label}
            </option>
          ))}
        </select>
        {(overdueOnly || status) && (
          <button
            type="button"
            onClick={() => {
              setOverdueOnly(false);
              setStatus("");
            }}
            className="text-[11.5px] text-(--ice-3) hover:text-(--ice)"
          >
            پاک کردن فیلترها
          </button>
        )}
      </div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      {!error && rows === null && <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>}

      {!error && rows !== null && (
        <>
          <div className="mb-2 text-[11px] text-(--ice-3)">{fa(rows.length)} قسط</div>
          {rows.length === 0 ? (
            <div className="rounded-2xl border border-(--edge) bg-(--pane) p-6 text-center text-[13px] text-(--ice-3)">موردی یافت نشد.</div>
          ) : (
            <div className="overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
              <table className="w-full border-collapse">
                <thead>
                  <tr>
                    {["بیمه‌نامه", "بیمه‌گذار", "قسط", "سررسید", "وضعیت", "مانده", ""].map((h) => (
                      <th key={h} className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                        {h}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {rows.map((r) => (
                    <tr
                      key={r.installmentId}
                      onClick={() =>
                        openTab({
                          navType: "policy-file",
                          page: "policy-file",
                          kind: "multi-record",
                          recordId: r.policyId,
                          title: r.policyNumber,
                          payload: { policyId: r.policyId },
                        })
                      }
                      className="cursor-pointer border-t border-(--edge) transition-colors first:border-t-0 hover:bg-(--hov)"
                    >
                      <td className="px-3 py-2.75 text-[13px] font-semibold">{r.policyNumber}</td>
                      <td className="px-3 py-2.75 text-[13px] text-(--ice-3)">{r.customerFullName}</td>
                      <td className="px-3 py-2.75 text-[13px]">{fa(r.seqNo)}</td>
                      <td className="px-3 py-2.75 text-[13px]">{toJalaliDisplay(r.dueDate)}</td>
                      <td className="px-3 py-2.75 text-[13px] text-(--ice-3)">{URGENCY_LABEL[r.urgency]}</td>
                      <td className="px-3 py-2.75 text-[13px] font-bold">{money(r.balance)}</td>
                      <td className="px-3 py-2.75 text-[13px]">
                        <button
                          type="button"
                          onClick={(e) => {
                            e.stopPropagation();
                            setPayingRow(r);
                          }}
                          className="rounded-[8px] border border-(--mint) bg-(--mint) px-2.5 py-1 text-[11px] font-semibold text-(--on-mint) transition-colors hover:brightness-105"
                        >
                          ثبت پرداخت
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </>
      )}

      {payingRow && (
        <RecordPaymentDialog
          installmentId={payingRow.installmentId}
          customerFullName={payingRow.customerFullName}
          suggestedAmount={payingRow.balance}
          onClose={() => setPayingRow(null)}
          onRecorded={reload}
        />
      )}
    </div>
  );
}
