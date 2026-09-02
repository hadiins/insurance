import { useCallback, useEffect, useState } from "react";
import { useTabsStore } from "../../app/store/tabsStore";
import { api, ApiError } from "../../lib/api";
import { fa, money } from "../../lib/persian";
import { toJalaliDisplay } from "../../lib/jalali";
import { useLiveReload } from "../shell/useLiveReload";
import { RecordPaymentDialog } from "./RecordPaymentDialog";

interface CountdownRowDto {
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

interface CountdownDashboardDto {
  owed: number;
  collected: number;
  shortfall: number;
  rows: CountdownRowDto[];
}

const URGENCY_LABEL: Record<CountdownRowDto["urgency"], string> = {
  Overdue: "معوق",
  Critical: "بحرانی",
  Warning: "هشدار",
  Upcoming: "سررسید نزدیک",
  Future: "آینده",
};

const URGENCY_PILL_CLASS: Record<CountdownRowDto["urgency"], string> = {
  Overdue: "bg-(--ember)/13 text-(--ember)",
  Critical: "bg-(--ember)/13 text-(--ember)",
  Warning: "bg-(--amber)/13 text-(--amber)",
  Upcoming: "bg-(--mint)/12 text-(--mint)",
  Future: "bg-(--mint)/12 text-(--mint)",
};

function daysLabel(row: CountdownRowDto): string {
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  const deadline = new Date(row.settlementDeadline);
  const diffDays = Math.round((deadline.getTime() - today.getTime()) / 86_400_000);

  if (row.urgency === "Overdue") return `${fa(Math.abs(diffDays))} روز تأخیر`;
  if (row.urgency === "Upcoming" || row.urgency === "Future") return `${fa(diffDays)} روز تا سررسید`;
  return diffDays <= 0 ? "سررسید مهلت" : `${fa(diffDays)} روز مانده`;
}

interface PnlSummaryDto {
  totalIncome: number;
  totalExpense: number;
  netProfit: number;
}

interface IncompleteProfileSummaryDto {
  total: number;
  withoutMobile: number;
  withoutNationalId: number;
}

interface RenewalWatchDto {
  id: string;
  customerId: string | null;
  customerFullName: string | null;
  prospectName: string | null;
  prospectMobile: string | null;
  insuranceLineName: string;
  currentInsurer: string | null;
  currentExpiryDate: string;
  notifyDaysBefore: number;
  status: string;
}

/** The watch window is already open: expiry is at or before today + NotifyDaysBefore. */
function isRenewalDue(w: RenewalWatchDto): boolean {
  const today = new Date();
  today.setHours(0, 0, 0, 0);
  const expiry = new Date(`${w.currentExpiryDate}T00:00:00`);
  return expiry.getTime() <= today.getTime() + w.notifyDaysBefore * 86_400_000;
}

export function TodayPage() {
  const openTab = useTabsStore((s) => s.openTab);
  const [data, setData] = useState<CountdownDashboardDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [payingRow, setPayingRow] = useState<CountdownRowDto | null>(null);
  const [pnl, setPnl] = useState<PnlSummaryDto | null>(null);
  const [incompleteProfiles, setIncompleteProfiles] = useState<IncompleteProfileSummaryDto | null>(null);
  const [dueRenewals, setDueRenewals] = useState<RenewalWatchDto[] | null>(null);

  const reload = useCallback(() => {
    api
      .get<CountdownDashboardDto>("/countdown")
      .then(setData)
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری اطلاعات"));
    api
      .get<RenewalWatchDto[]>("/renewal-watches?status=Watching")
      .then((rows) => setDueRenewals(rows.filter(isRenewalDue)))
      .catch(() => setDueRenewals(null));
  }, []);

  useLiveReload(reload);

  useEffect(() => {
    reload();
  }, [reload]);

  useEffect(() => {
    const now = new Date();
    const from = new Date(now.getFullYear(), now.getMonth(), 1).toISOString().slice(0, 10);
    const to = now.toISOString().slice(0, 10);
    api
      .get<PnlSummaryDto>(`/reports/pnl?from=${from}&to=${to}&basis=Accrual`)
      .then(setPnl)
      .catch(() => setPnl(null));
  }, []);

  useEffect(() => {
    api
      .get<IncompleteProfileSummaryDto>("/customers/incomplete-summary")
      .then(setIncompleteProfiles)
      .catch(() => setIncompleteProfiles(null));
  }, []);

  const overdueOrCriticalCount = data?.rows.filter((r) => r.urgency === "Overdue" || r.urgency === "Critical").length ?? 0;

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">
        شمارش‌معکوس <em className="font-extralight not-italic text-(--ice-2)">تسویه</em>
      </h2>
      <div className="mb-4.5 text-xs text-(--ice-3)">اقساطی که باید ظرف مهلت مقرر به بیمه‌گر تسویه شوند</div>

      <div className="mb-4.5 rounded-xl border border-(--mint)/22 bg-(--mint)/7 p-4 text-[12.5px] text-(--ice-2)">
        این تب <b className="font-bold text-(--mint)">سنجاق</b> شده و بسته نمی‌شود. روی هر ردیف کلیک کنید تا در تب
        جدید باز شود — این تب دست‌نخورده می‌ماند.
      </div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      {pnl !== null && (
        <div
          onClick={() =>
            openTab({ navType: "reports-pnl", page: "pnl", kind: "singleton", title: "سود و زیان" })
          }
          className="mb-4.5 flex cursor-pointer items-center justify-between rounded-[14px] border border-(--edge) bg-(--pane) p-3.5 transition-colors hover:bg-(--hov)"
        >
          <div>
            <div className="mb-1 text-[10px] tracking-[0.16em] text-(--ice-3)">سود و زیان این ماه (تعهدی)</div>
            <div className={`text-[19px] font-extrabold ${pnl.netProfit >= 0 ? "text-(--mint)" : "text-(--ember)"}`}>
              {money(pnl.netProfit)}
            </div>
          </div>
          <div className="text-[11px] text-(--ice-3)">مشاهدهٔ گزارش کامل ←</div>
        </div>
      )}

      {incompleteProfiles !== null && incompleteProfiles.total > 0 && (
        <div
          onClick={() =>
            openTab({ navType: "customer-completion", page: "customer-completion", kind: "singleton", title: "تکمیل پروندهٔ مشتریان" })
          }
          className="mb-4.5 cursor-pointer rounded-[14px] border border-(--amber)/25 bg-(--amber)/6 p-3.5 transition-colors hover:bg-(--amber)/10"
        >
          <div className="mb-2 flex items-center justify-between">
            <div className="text-[13px] font-bold text-(--amber)">⚠ {fa(incompleteProfiles.total)} مشتری اطلاعات ناقص دارند</div>
            <div className="text-[11px] text-(--ice-3)">تکمیل پرونده‌ها ←</div>
          </div>
          <div className="flex gap-5 text-[11.5px] text-(--ice-3)">
            <span>
              بدون شمارهٔ موبایل: <b className="text-(--ice-2)">{fa(incompleteProfiles.withoutMobile)}</b> ← یادآوری پیامکی کار نمی‌کند
            </span>
            <span>
              بدون کد ملی: <b className="text-(--ice-2)">{fa(incompleteProfiles.withoutNationalId)}</b> ← اعتبارسنجی ممکن نیست
            </span>
          </div>
        </div>
      )}

      {dueRenewals !== null && dueRenewals.length > 0 && (
        <div className="mb-4.5 rounded-[14px] border border-(--edge) bg-(--pane) p-3.5">
          <div className="mb-2 flex items-center justify-between">
            <div className="text-[13px] font-bold text-(--ice)">
              🔁 سررسیدهای تمدید نزدیک — {fa(dueRenewals.length)} مورد
            </div>
            <button
              type="button"
              onClick={() =>
                openTab({
                  navType: "renewal-watches",
                  page: "renewal-watches",
                  kind: "singleton",
                  title: "سررسید تمدید",
                })
              }
              className="text-[11px] text-(--ice-3) transition-colors hover:text-(--ice)"
            >
              مشاهدهٔ همه ←
            </button>
          </div>
          <div className="overflow-hidden rounded-[10px] border border-(--edge)">
            <table className="w-full border-collapse">
              <thead>
                <tr>
                  <th className="border-b border-(--edge) px-3 py-2 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                    بیمه‌گذار
                  </th>
                  <th className="border-b border-(--edge) px-3 py-2 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                    رشته
                  </th>
                  <th className="border-b border-(--edge) px-3 py-2 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                    بیمه‌گر فعلی
                  </th>
                  <th className="border-b border-(--edge) px-3 py-2 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                    تاریخ انقضا
                  </th>
                </tr>
              </thead>
              <tbody>
                {dueRenewals.map((w) => (
                  <tr
                    key={w.id}
                    onClick={() =>
                      openTab({
                        navType: "renewal-watches",
                        page: "renewal-watches",
                        kind: "singleton",
                        title: "سررسید تمدید",
                      })
                    }
                    className="cursor-pointer border-t border-(--edge) transition-colors first:border-t-0 hover:bg-(--hov)"
                  >
                    <td className="px-3 py-2 text-[13px] font-semibold">
                      {w.customerFullName ?? w.prospectName ?? "—"}
                    </td>
                    <td className="px-3 py-2 text-[12.5px] text-(--ice-3)">{w.insuranceLineName}</td>
                    <td className="px-3 py-2 text-[12.5px] text-(--ice-3)">{w.currentInsurer ?? "—"}</td>
                    <td className="px-3 py-2 text-[12.5px] font-bold">{toJalaliDisplay(w.currentExpiryDate)}</td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </div>
      )}

      {!error && data === null && <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>}

      {!error && data !== null && (
        <>
          <div className="mb-4 grid grid-cols-4 gap-3">
            <Fig label="کسری" value={money(data.shortfall)} caption="از جیب نماینده" tone="ember" />
            <Fig
              label="بدهی به بیمه‌گر"
              value={money(data.owed)}
              caption={`${fa(data.rows.length)} قسط`}
              tone="amber"
            />
            <Fig label="وصول‌شده" value={money(data.collected)} caption="از همین اقساط" />
            <Fig label="معوق/بحرانی" value={fa(overdueOrCriticalCount)} caption="نیازمند اقدام فوری" tone="ember" />
          </div>

          {data.rows.length === 0 ? (
            <div className="rounded-2xl border border-(--edge) bg-(--pane) p-6 text-center text-[13px] text-(--ice-3)">
              هیچ قسطی در بازهٔ فعال تسویه نیست.
            </div>
          ) : (
            <div className="overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
              <table className="w-full border-collapse">
                <thead>
                  <tr>
                    <th className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                      بیمه‌گذار
                    </th>
                    <th className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                      وضعیت
                    </th>
                    <th className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                      قسط
                    </th>
                    <th className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                      مبلغ
                    </th>
                    <th className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)" />
                  </tr>
                </thead>
                <tbody>
                  {data.rows.map((r) => (
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
                      <td className="px-3 py-2.75 text-[13px] font-semibold">{r.customerFullName}</td>
                      <td className="px-3 py-2.75 text-[13px]">
                        <span className={`rounded-full px-2.5 py-0.5 text-[11px] font-semibold ${URGENCY_PILL_CLASS[r.urgency]}`}>
                          {URGENCY_LABEL[r.urgency]}
                        </span>
                      </td>
                      <td className="px-3 py-2.75 text-[13px] text-(--ice-3)">{daysLabel(r)}</td>
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

function Fig({
  label,
  value,
  caption,
  tone,
}: {
  label: string;
  value: string;
  caption: string;
  tone?: "ember" | "amber";
}) {
  const valueColor = tone === "ember" ? "text-(--ember)" : tone === "amber" ? "text-(--amber)" : "text-(--ice)";
  const barColor = tone === "ember" ? "bg-(--ember)" : tone === "amber" ? "bg-(--amber)" : "bg-(--mint)";
  return (
    <div className="relative overflow-hidden rounded-[14px] border border-(--edge) bg-(--pane) p-3.5">
      <span className={`absolute start-0 top-0 h-0.5 w-7.5 ${barColor}`} />
      <div className="mb-1 text-[10px] tracking-[0.16em] text-(--ice-3)">{label}</div>
      <div className={`text-[23px] font-extrabold tracking-tight ${valueColor}`}>{value}</div>
      <div className="text-[11px] text-(--ice-3)">{caption}</div>
    </div>
  );
}
