import { useCallback, useEffect, useMemo, useState } from "react";
import { AlarmIcon, UserListIcon } from "@phosphor-icons/react";
import type { Icon } from "@phosphor-icons/react";
import { useTabsStore } from "../../app/store/tabsStore";
import { api, ApiError } from "../../lib/api";
import { fa, money } from "../../lib/persian";
import { toJalaliDisplay } from "../../lib/jalali";
import { useLiveReload } from "../shell/useLiveReload";
import { EmptyState } from "../../components/EmptyState";
import { ErrorState } from "../../components/ErrorState";
import { SkeletonBlock, SkeletonRail, SkeletonRows } from "../../components/Skeleton";
import { StatusBadge } from "../../components/StatusBadge";
import { Table, Td, Th, Tr } from "../../components/Table";
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

const URGENCY_TONE: Record<CountdownRowDto["urgency"], "mint" | "ember" | "amber" | "neutral"> = {
  Overdue: "ember",
  Critical: "ember",
  Warning: "amber",
  Upcoming: "mint",
  Future: "mint",
};

/** Most urgent first. The server's order is not a promise, and the top row of this table is
 * what the agent acts on next, so it is pinned here rather than trusted. */
const URGENCY_RANK: Record<CountdownRowDto["urgency"], number> = {
  Overdue: 0,
  Critical: 1,
  Warning: 2,
  Upcoming: 3,
  Future: 4,
};

function startOfToday(): number {
  const d = new Date();
  d.setHours(0, 0, 0, 0);
  return d.getTime();
}

function daysLabel(row: CountdownRowDto): string {
  const diffDays = Math.round((new Date(row.settlementDeadline).getTime() - startOfToday()) / 86_400_000);

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
  withoutAddress: number;
  withoutPostalCode: number;
  withoutName: number;
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
  const expiry = new Date(`${w.currentExpiryDate}T00:00:00`).getTime();
  return expiry <= startOfToday() + w.notifyDaysBefore * 86_400_000;
}

export function TodayPage() {
  const openTab = useTabsStore((s) => s.openTab);
  const [data, setData] = useState<CountdownDashboardDto | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [payingRow, setPayingRow] = useState<CountdownRowDto | null>(null);
  const [pnl, setPnl] = useState<PnlSummaryDto | null>(null);
  const [incompleteProfiles, setIncompleteProfiles] = useState<IncompleteProfileSummaryDto | null>(null);
  const [dueRenewals, setDueRenewals] = useState<RenewalWatchDto[] | null>(null);
  const [failedPanels, setFailedPanels] = useState<string[]>([]);

  /** One loader for the whole page. The secondary panels used to fetch once on mount and
   * never again, so a live reload refreshed the countdown while the P&L and profile
   * counters kept showing the state from whenever the tab was first opened. They now
   * share this path. A failed panel is named rather than silently dropped (rule 15). */
  const load = useCallback(() => {
    api
      .get<CountdownDashboardDto>("/countdown")
      .then((d) => {
        setData(d);
        setError(null);
      })
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری شمارش‌معکوس"));

    const now = new Date();
    const from = new Date(now.getFullYear(), now.getMonth(), 1).toISOString().slice(0, 10);
    const to = now.toISOString().slice(0, 10);

    const panels: [string, Promise<unknown>][] = [
      [
        "سود و زیان",
        api
          .get<PnlSummaryDto>(`/reports/pnl?from=${from}&to=${to}&basis=Accrual`)
          .then((d) => {
            setPnl(d);
            return d;
          })
          .catch((err) => {
            setPnl(null);
            throw err;
          }),
      ],
      [
        "پرونده‌های ناقص",
        api
          .get<IncompleteProfileSummaryDto>("/customers/incomplete-summary")
          .then((d) => {
            setIncompleteProfiles(d);
            return d;
          })
          .catch((err) => {
            setIncompleteProfiles(null);
            throw err;
          }),
      ],
      [
        "سررسید تمدید",
        api
          .get<RenewalWatchDto[]>("/renewal-watches?status=Watching")
          .then((rows) => {
            setDueRenewals(
              rows.filter(isRenewalDue).sort((a, b) => a.currentExpiryDate.localeCompare(b.currentExpiryDate))
            );
            return rows;
          })
          .catch((err) => {
            setDueRenewals(null);
            throw err;
          }),
      ],
    ];

    void Promise.allSettled(panels.map(([, p]) => p)).then((results) => {
      setFailedPanels(
        results.flatMap((r, i) => (r.status === "rejected" ? [panels[i][0]] : []))
      );
    });
  }, []);

  useLiveReload(load);

  useEffect(() => {
    load();
  }, [load]);

  const rows = useMemo(() => {
    if (data === null) return [];
    return [...data.rows].sort(
      (a, b) =>
        URGENCY_RANK[a.urgency] - URGENCY_RANK[b.urgency] ||
        new Date(a.settlementDeadline).getTime() - new Date(b.settlementDeadline).getTime()
    );
  }, [data]);

  const overdueOrCriticalCount = rows.filter((r) => r.urgency === "Overdue" || r.urgency === "Critical").length;
  const collectedPercent = data && data.owed > 0 ? Math.min(100, Math.round((data.collected / data.owed) * 100)) : 0;

  const needsAttention = [
    incompleteProfiles !== null && incompleteProfiles.total > 0 ? 1 : 0,
    dueRenewals !== null && dueRenewals.length > 0 ? 1 : 0,
  ].reduce((a, b) => a + b, 0);

  return (
    <div>
      <div className="mb-4 flex flex-wrap items-end justify-between gap-3">
        <div>
          <h2 className="text-xl font-extrabold tracking-tight text-(--ice)">
            شمارش‌معکوس <em className="font-extralight not-italic text-(--ice-2)">تسویه</em>
          </h2>
          <div className="mt-0.5 text-[12.5px] text-(--ice-3)">اقساطی که باید ظرف مهلت مقرر به بیمه‌گر تسویه شوند</div>
        </div>
        {pnl && (
          <button
            type="button"
            onClick={() => openTab({ navType: "reports-pnl", page: "pnl", kind: "singleton", title: "سود و زیان" })}
            className="rounded-(--r) px-2 py-1 text-end transition-colors hover:bg-(--hov)"
          >
            <div className="text-[11px] font-medium text-(--ice-3)">سود و زیان این ماه</div>
            <div className={`text-[18px] font-extrabold tracking-tight ${pnl.netProfit >= 0 ? "text-(--moss)" : "text-(--ember)"}`}>
              {money(pnl.netProfit)}
            </div>
          </button>
        )}
      </div>

      {data !== null && (error !== null || failedPanels.length > 0) && (
        <div className="mb-3.5 flex flex-wrap items-center justify-between gap-3 rounded-(--r) border border-(--ember)/30 bg-(--ember)/10 px-3.5 py-2.5">
          <div>
            <div className="text-[12.5px] text-(--ember)">
              {error ?? "بارگذاری بخشی از این صفحه ناموفق بود؛ اعداد زیر ممکن است قدیمی باشند."}
            </div>
            {failedPanels.length > 0 && (
              <div className="mt-0.5 text-[11.5px] text-(--ice-3)">
                بارگذاری این بخش‌ها ناموفق بود: {failedPanels.join("، ")}
              </div>
            )}
          </div>
          <button
            type="button"
            onClick={load}
            className="shrink-0 rounded-(--r) border border-(--ember)/45 px-2.5 py-1 text-[11.5px] font-semibold text-(--ember) transition-colors hover:bg-(--ember)/15"
          >
            بازخوانی
          </button>
        </div>
      )}

      {!error && data === null && (
        <div aria-busy>
          <SkeletonRail />
          <SkeletonBlock className="mt-4 h-1 w-full" />
          <div className="mt-4">
            <SkeletonRows rows={6} cols={4} />
          </div>
        </div>
      )}

      {error !== null && data === null && (
        <ErrorState
          title="شمارش‌معکوس تسویه بارگذاری نشد"
          description={
            failedPanels.length > 0
              ? `${error} بارگذاری این بخش‌ها هم ناموفق بود: ${failedPanels.join("، ")}`
              : error
          }
          onRetry={load}
        />
      )}

      {data !== null && (
        <>
          <div className="mb-3.5 grid grid-cols-4 overflow-hidden rounded-(--r-lg) border border-(--edge) bg-(--pane)">
            <Fig
              label="کسری"
              value={money(data.shortfall)}
              caption="از جیب نماینده"
              tone={data.shortfall > 0 ? "ember" : undefined}
            />
            <Fig
              label="بدهی به بیمه‌گر"
              value={money(data.owed)}
              caption={`${fa(data.rows.length)} قسط`}
              tone={data.owed > 0 ? "amber" : undefined}
            />
            <Fig label="وصول‌شده" value={money(data.collected)} caption="از همین اقساط" tone="moss" />
            <Fig
              label="معوق یا بحرانی"
              value={fa(overdueOrCriticalCount)}
              caption="نیازمند اقدام فوری"
              tone={overdueOrCriticalCount > 0 ? "ember" : undefined}
            />
          </div>

          <div className="mb-5 flex items-center gap-3">
            <div className="shrink-0 text-[11.5px] text-(--ice-3)">وصول مطالبات پنجرهٔ فعال</div>
            <div className="h-0.5 flex-1 overflow-hidden rounded-full bg-(--edge)">
              <div
                className="h-full rounded-full bg-(--moss) transition-[width] duration-500"
                style={{ width: `${collectedPercent}%` }}
              />
            </div>
            <div className="shrink-0 text-[12.5px] font-bold text-(--moss)">{fa(collectedPercent)}٪</div>
          </div>

          {needsAttention > 0 && (
            <>
              <div className="mb-2 flex items-center justify-between">
                <div className="text-[13px] font-bold text-(--ice)">نیازمند توجه</div>
                <div className="text-[11.5px] text-(--ice-3)">{fa(needsAttention)} مورد</div>
              </div>
              <div className="mb-5 overflow-hidden rounded-(--r-lg) border border-(--edge) bg-(--pane)">
                {incompleteProfiles !== null && incompleteProfiles.total > 0 && (
                  <AttentionRow
                    icon={UserListIcon}
                    title={`${fa(incompleteProfiles.total)} مشتری اطلاعات ناقص دارند`}
                    action="تکمیل پرونده‌ها"
                    onAction={() =>
                      openTab({
                        navType: "customer-completion",
                        page: "customer-completion",
                        kind: "singleton",
                        title: "تکمیل پروندهٔ مشتریان",
                      })
                    }
                  >
                    <div className="grid grid-cols-2 gap-x-6 gap-y-1 sm:grid-cols-3">
                      <Gap label="بدون شمارهٔ موبایل" count={incompleteProfiles.withoutMobile} note="یادآوری پیامکی کار نمی‌کند" />
                      <Gap label="بدون کد ملی" count={incompleteProfiles.withoutNationalId} note="اعتبارسنجی ممکن نیست" />
                      <Gap label="بدون آدرس" count={incompleteProfiles.withoutAddress} />
                      <Gap label="بدون کد پستی" count={incompleteProfiles.withoutPostalCode} />
                      <Gap label="بدون نام یا نام خانوادگی" count={incompleteProfiles.withoutName} />
                    </div>
                  </AttentionRow>
                )}

                {dueRenewals !== null && dueRenewals.length > 0 && (
                  <AttentionRow
                    icon={AlarmIcon}
                    title={`${fa(dueRenewals.length)} بیمه‌نامه در پنجرهٔ تمدید`}
                    action="مشاهدهٔ همه"
                    onAction={() =>
                      openTab({
                        navType: "renewal-watches",
                        page: "renewal-watches",
                        kind: "singleton",
                        title: "سررسید تمدید",
                      })
                    }
                  >
                    <div className="text-[11.5px] leading-relaxed text-(--ice-3)">
                      نزدیک‌ترین انقضا {toJalaliDisplay(dueRenewals[0].currentExpiryDate)}
                      {"؛ "}
                      {dueRenewals
                        .slice(0, 3)
                        .map((w) => w.customerFullName ?? w.prospectName ?? "بدون نام")
                        .join("، ")}
                      {dueRenewals.length > 3 && ` و ${fa(dueRenewals.length - 3)} مورد دیگر`}
                    </div>
                  </AttentionRow>
                )}
              </div>
            </>
          )}

          {rows.length === 0 ? (
            <EmptyState
              title="هیچ قسطی در پنجرهٔ فعال تسویه نیست"
              description="در حال حاضر قسط سررسیدشده‌ای که نیاز به تسویه داشته باشد وجود ندارد."
            />
          ) : (
            <>
              <div className="mb-2 flex items-center justify-between">
                <div className="text-[13px] font-bold text-(--ice)">اقساط در پنجرهٔ فعال تسویه</div>
                <div className="text-[11.5px] text-(--ice-3)">{fa(rows.length)} قسط</div>
              </div>
              <Table>
                <thead>
                  <tr>
                    <Th>بیمه‌گذار</Th>
                    <Th>وضعیت</Th>
                    <Th>مهلت تسویه</Th>
                    <Th>مانده</Th>
                    <Th />
                  </tr>
                </thead>
                <tbody>
                  {rows.map((r) => (
                    <Tr
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
                    >
                      <Td className="py-2.75">
                        <div className="font-semibold text-(--ice)">{r.customerFullName}</div>
                        <div className="mt-0.5 text-[11.5px] text-(--ice-3)">
                          بیمه‌نامهٔ {fa(r.policyNumber)} · قسط {fa(r.seqNo)}
                        </div>
                      </Td>
                      <Td className="py-2.75">
                        <StatusBadge tone={URGENCY_TONE[r.urgency]}>{URGENCY_LABEL[r.urgency]}</StatusBadge>
                      </Td>
                      <Td className="py-2.75">
                        <div className="text-(--ice-2)">{toJalaliDisplay(r.settlementDeadline)}</div>
                        <div className="mt-0.5 text-[11.5px] text-(--ice-3)">{daysLabel(r)}</div>
                      </Td>
                      <Td className="py-2.75 font-bold">{money(r.balance)}</Td>
                      <Td className="py-2.75">
                        <button
                          type="button"
                          onClick={(e) => {
                            e.stopPropagation();
                            setPayingRow(r);
                          }}
                          className="rounded-(--r) border border-(--mint) px-2.5 py-1 text-[11.5px] font-semibold text-(--mint) transition-colors hover:bg-(--mint) hover:text-(--on-mint)"
                        >
                          ثبت پرداخت
                        </button>
                      </Td>
                    </Tr>
                  ))}
                </tbody>
              </Table>
            </>
          )}
        </>
      )}

      <div className="mt-5 text-[11.5px] leading-relaxed text-(--ice-3)">
        این تب سنجاق شده و بسته نمی‌شود. کلیک روی هر ردیف، بیمه‌نامه را در تب جدید باز می‌کند و این تب دست‌نخورده می‌ماند.
      </div>

      {payingRow && (
        <RecordPaymentDialog
          installmentId={payingRow.installmentId}
          customerFullName={payingRow.customerFullName}
          suggestedAmount={payingRow.balance}
          onClose={() => setPayingRow(null)}
          onRecorded={load}
        />
      )}
    </div>
  );
}

/** A single hairline-separated advisory row. No card chrome: the pane already groups them.
 * The icon tile marks which kind of advisory this is while the eye scans the group. */
function AttentionRow({
  icon: Icon,
  title,
  action,
  onAction,
  children,
}: {
  icon: Icon;
  title: string;
  action: string;
  onAction: () => void;
  children: React.ReactNode;
}) {
  return (
    <div className="flex items-start gap-3 border-t border-(--edge) px-3.5 py-3 first:border-t-0">
      <div className="mt-0.5 flex h-8 w-8 shrink-0 items-center justify-center rounded-(--r) bg-(--fld)">
        <Icon size={17} className="text-(--ice-2)" />
      </div>
      <div className="min-w-0 flex-1">
        <div className="mb-1.5 text-[13px] font-semibold text-(--ice)">{title}</div>
        {children}
      </div>
      <button
        type="button"
        onClick={onAction}
        className="shrink-0 rounded-(--r) px-2 py-1 text-[11.5px] font-semibold text-(--mint) transition-colors hover:bg-(--hov)"
      >
        {action}
      </button>
    </div>
  );
}

function Gap({ label, count, note }: { label: string; count: number; note?: string }) {
  return (
    <div className="text-[11.5px] leading-relaxed">
      <span className="text-(--ice-3)">{label} </span>
      <b className={count > 0 ? "font-bold text-(--amber)" : "font-bold text-(--ice-2)"}>{fa(count)}</b>
      {note && count > 0 && <span className="text-(--ice-3)"> · {note}</span>}
    </div>
  );
}

/** One figure in the status rail. The rail pane draws the hairline between columns. */
function Fig({
  label,
  value,
  caption,
  tone,
}: {
  label: string;
  value: string;
  caption: string;
  tone?: "ember" | "amber" | "moss";
}) {
  const valueColor =
    tone === "ember"
      ? "text-(--ember)"
      : tone === "amber"
        ? "text-(--amber)"
        : tone === "moss"
          ? "text-(--moss)"
          : "text-(--ice)";
  return (
    <div className="border-s border-(--edge) p-3.5 first:border-s-0">
      <div className="mb-0.5 text-[11px] font-medium text-(--ice-3)">{label}</div>
      <div className={`text-[19px] font-extrabold tracking-tight ${valueColor}`}>{value}</div>
      <div className="text-[11.5px] text-(--ice-3)">{caption}</div>
    </div>
  );
}
