import { useCallback, useEffect, useMemo, useState } from "react";
import {
  AlarmIcon,
  ArrowSquareOutIcon,
  ArrowsClockwiseIcon,
  DownloadSimpleIcon,
  PhoneIcon,
  PlusCircleIcon,
  UserListIcon,
} from "@phosphor-icons/react";
import type { Icon } from "@phosphor-icons/react";
import { useTabsStore } from "../../app/store/tabsStore";
import { useAuthStore } from "../../app/store/authStore";
import { api, ApiError, getActiveOrgId, getToken } from "../../lib/api";
import { fa, money } from "../../lib/persian";
import { toJalaliDisplay, todayJalaliParts } from "../../lib/jalali";
import { useLiveReload } from "../shell/useLiveReload";
import { ErrorState } from "../../components/ErrorState";
import { SkeletonBlock, SkeletonRail, SkeletonRows } from "../../components/Skeleton";
import { StatusBadge } from "../../components/StatusBadge";
import { Table, Td, Th, Tr } from "../../components/Table";
import { BTN_PRIMARY_SM, BTN_SECONDARY_SM } from "../../components/form";
import { GroupedTrendBars, type GroupedTrendDay } from "./GroupedTrendBars";
import { RecordPaymentDialog, RECORD_PAYMENT_PANEL_ID } from "./RecordPaymentDialog";
import { LogContactDialog } from "./LogContactDialog";

interface TodayFiguresDto {
  collectedToday: number;
  collectedTodayDeltaPct: number | null;
  collectedLast7Days: number[];
  dueTodayCount: number;
  dueTodayUnpaidCount: number;
  dueTodaySettledCount: number;
  overdue30Amount: number;
  overdue30Count: number;
  chequesInFlightCount: number;
  chequesBouncedCount: number;
}

interface TodayTrendPointDto {
  date: string;
  collected: number;
  goalRemaining: number;
}

interface TodayGoalDto {
  monthlyGoal: number | null;
  dailyTarget: number | null;
  monthCollected: number;
  monthProgressPct: number | null;
}

interface TodayActivityDto {
  kind: string;
  tone: string;
  title: string;
  detail: string;
}

interface TodaySyncDto {
  lastSyncAt: string | null;
  lastSyncAgoMinutes: number | null;
  openMismatchCount: number;
}

interface TodayDashboardDto {
  figures: TodayFiguresDto;
  trend: TodayTrendPointDto[];
  goal: TodayGoalDto;
  activity: TodayActivityDto[];
  sync: TodaySyncDto;
  activeUsers: number;
}

type Urgency = "Overdue" | "Critical" | "Warning" | "Upcoming" | "Future";

interface WorklistRowDto {
  installmentId: string;
  policyId: string;
  policyNumber: string;
  customerFullName: string;
  customerMobile: string | null;
  seqNo: number;
  dueDate: string;
  settlementDeadline: string;
  amount: number;
  paidAmount: number;
  balance: number;
  status: string;
  urgency: Urgency;
  seqTotal: number;
}

interface CountsDto {
  total: number;
  totalBalance: number;
  buckets: { urgency: string; count: number; balance: number }[];
  filters: { filter: string; count: number }[];
}

interface CountdownDashboardDto {
  owed: number;
  collected: number;
  shortfall: number;
  rows: {
    installmentId: string;
    policyId: string;
    policyNumber: string;
    customerFullName: string;
    seqNo: number;
    settlementDeadline: string;
    balance: number;
    urgency: Urgency;
  }[];
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
  customerFullName: string | null;
  prospectName: string | null;
  currentExpiryDate: string;
  notifyDaysBefore: number;
  status: string;
}

interface PendingRemittanceRowDto {
  policyId: string;
  policyNumber: string;
  customerFullName: string;
  amount: number;
  collectedOn: string;
}

interface MissingSerialsReportDto {
  year: number;
  registeredCount: number;
  missingCount: number;
  missing: string[];
}

interface ManualReviewDto {
  id: string;
  customerName: string;
  score: number;
  riskLevel: string;
  statusFa: string;
}

const URGENCY_LABEL: Record<Urgency, string> = {
  Overdue: "معوق",
  Critical: "بحرانی",
  Warning: "هشدار",
  Upcoming: "سررسید نزدیک",
  Future: "آینده",
};

const URGENCY_TONE: Record<Urgency, "mint" | "ember" | "amber" | "neutral"> = {
  Overdue: "ember",
  Critical: "ember",
  Warning: "amber",
  Upcoming: "mint",
  Future: "mint",
};

/** Most urgent first. The server's order is not a promise, and the top row of these lists is what
 * the agent acts on next, so it is pinned here rather than trusted. */
const URGENCY_RANK: Record<Urgency, number> = {
  Overdue: 0,
  Critical: 1,
  Warning: 2,
  Upcoming: 3,
  Future: 4,
};

/** Labels for the worklist chips. The *counts* come from the server; these strings do not, so a
 * chip the server adds can never render as a blank button — an unknown key falls back to itself. */
const CHIP_LABEL: Record<string, string> = {
  all: "همه",
  overdue: "معوق",
  cheque: "چک‌دار",
  promise: "قول پرداخت",
  noContact: "بدون تماس",
};

const CHIP_ORDER = ["all", "overdue", "cheque", "promise", "noContact"];

/** How many worklist rows a page shows. The server caps the whole list at 300; this only splits
 * what it sent, so the pager never claims to reach rows it does not have. */
const PAGE_SIZE = 8;

const COUNTDOWN_PREVIEW = 6;

function startOfToday(): number {
  const d = new Date();
  d.setHours(0, 0, 0, 0);
  return d.getTime();
}

function daysLabel(row: { settlementDeadline: string; urgency: Urgency }): string {
  const diffDays = Math.round((new Date(row.settlementDeadline).getTime() - startOfToday()) / 86_400_000);

  if (row.urgency === "Overdue") return `${fa(Math.abs(diffDays))} روز تأخیر`;
  if (row.urgency === "Upcoming" || row.urgency === "Future") return `${fa(diffDays)} روز تا سررسید`;
  return diffDays <= 0 ? "سررسید مهلت" : `${fa(diffDays)} روز مانده`;
}

/** The watch window is already open: expiry is at or before today + NotifyDaysBefore. */
function isRenewalDue(w: RenewalWatchDto): boolean {
  const expiry = new Date(`${w.currentExpiryDate}T00:00:00`).getTime();
  return expiry <= startOfToday() + w.notifyDaysBefore * 86_400_000;
}

function agoLabel(minutes: number | null): string {
  if (minutes === null) return "نامشخص";
  if (minutes < 1) return "همین حالا";
  if (minutes < 60) return `${fa(minutes)} دقیقه پیش`;
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return `${fa(hours)} ساعت پیش`;
  return `${fa(Math.floor(hours / 24))} روز پیش`;
}

export function TodayPage() {
  const openTab = useTabsStore((s) => s.openTab);
  const permissions = useAuthStore((s) => s.user?.permissions);
  const canWrite = permissions?.includes("Policy.Write") ?? false;
  const canSeeFinance = permissions?.includes("Finance.Read") ?? false;

  const [dash, setDash] = useState<TodayDashboardDto | null>(null);
  const [dashError, setDashError] = useState<string | null>(null);
  const [countdown, setCountdown] = useState<CountdownDashboardDto | null>(null);
  const [countdownFailed, setCountdownFailed] = useState(false);
  const [rows, setRows] = useState<WorklistRowDto[]>([]);
  const [rowsError, setRowsError] = useState<string | null>(null);
  const [rowsLoading, setRowsLoading] = useState(true);
  const [counts, setCounts] = useState<CountsDto | null>(null);
  const [chip, setChip] = useState("all");
  const [pageIndex, setPageIndex] = useState(0);
  const [paying, setPaying] = useState<WorklistRowDto | null>(null);
  const [loggingContact, setLoggingContact] = useState<WorklistRowDto | null>(null);
  const [exporting, setExporting] = useState(false);
  const [exportError, setExportError] = useState<string | null>(null);
  const [showAllCountdown, setShowAllCountdown] = useState(false);

  const [incompleteProfiles, setIncompleteProfiles] = useState<IncompleteProfileSummaryDto | null>(null);
  const [dueRenewals, setDueRenewals] = useState<RenewalWatchDto[] | null>(null);
  const [remittances, setRemittances] = useState<PendingRemittanceRowDto[] | null>(null);
  const [missingSerials, setMissingSerials] = useState<MissingSerialsReportDto | null>(null);
  const [reviews, setReviews] = useState<ManualReviewDto[] | null>(null);
  const [pnl, setPnl] = useState<PnlSummaryDto | null>(null);

  const [failedPanels, setFailedPanels] = useState<string[]>([]);

  /** One loader for the whole page. A panel that failed is named in the banner rather than silently
   * dropped (rule 15), and a panel the caller has no permission for is never requested at all —
   * a permanent «دسترسی ندارید» box on an otherwise healthy page is noise, not honesty. */
  const load = useCallback(() => {
    setRowsLoading(true);

    const panels: [string, Promise<unknown>][] = [
      [
        "ارقام امروز",
        api
          .get<TodayDashboardDto>("/today")
          .then((d) => {
            setDash(d);
            setDashError(null);
            return d;
          })
          .catch((err) => {
            setDashError(err instanceof ApiError ? err.message : "خطا در بارگذاری ارقام امروز");
            throw err;
          }),
      ],
      [
        "شمارش‌معکوس تسویه",
        api
          .get<CountdownDashboardDto>("/countdown")
          .then((d) => {
            setCountdown(d);
            setCountdownFailed(false);
            return d;
          })
          .catch((err) => {
            // The previous figures stay on screen; the panel says they may be stale rather than
            // going blank, because an empty countdown reads as "nothing is due".
            setCountdownFailed(true);
            throw err;
          }),
      ],
      [
        "فهرست اقساط",
        api
          .get<WorklistRowDto[]>(`/installments?filter=${encodeURIComponent(chip)}`)
          .then((list) => {
            setRows(list);
            setRowsError(null);
            return list;
          })
          .catch((err) => {
            setRows([]);
            setRowsError(err instanceof ApiError ? err.message : "خطا در بارگذاری فهرست اقساط");
            throw err;
          })
          .finally(() => setRowsLoading(false)),
      ],
      [
        "شمارش فهرست",
        api
          .get<CountsDto>("/installments/counts")
          .then((d) => {
            setCounts(d);
            return d;
          })
          .catch((err) => {
            setCounts(null);
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
          .then((list) => {
            setDueRenewals(
              list.filter(isRenewalDue).sort((a, b) => a.currentExpiryDate.localeCompare(b.currentExpiryDate))
            );
            return list;
          })
          .catch((err) => {
            setDueRenewals(null);
            throw err;
          }),
      ],
      [
        "بررسی‌های دستی",
        api
          .get<ManualReviewDto[]>("/risk/manual-reviews?status=Pending")
          .then((list) => {
            setReviews(list);
            return list;
          })
          .catch((err) => {
            setReviews(null);
            throw err;
          }),
      ],
      [
        "شماره‌های جا افتاده",
        api
          .get<MissingSerialsReportDto>(`/reports/missing-serials?year=${todayJalaliParts().jy}`)
          .then((d) => {
            setMissingSerials(d);
            return d;
          })
          .catch((err) => {
            setMissingSerials(null);
            throw err;
          }),
      ],
    ];

    if (canSeeFinance) {
      const now = new Date();
      const from = new Date(now.getFullYear(), now.getMonth(), 1).toISOString().slice(0, 10);
      const to = now.toISOString().slice(0, 10);

      panels.push([
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
      ]);

      panels.push([
        "تسویه با بیمه‌گر",
        api
          .get<PendingRemittanceRowDto[]>("/insurer-remittances/pending")
          .then((list) => {
            setRemittances(list);
            return list;
          })
          .catch((err) => {
            setRemittances(null);
            throw err;
          }),
      ]);
    }

    void Promise.allSettled(panels.map(([, p]) => p)).then((results) => {
      setFailedPanels(results.flatMap((r, i) => (r.status === "rejected" ? [panels[i][0]] : [])));
    });
  }, [chip, canSeeFinance]);

  useLiveReload(load);

  useEffect(() => {
    load();
  }, [load]);

  useEffect(() => {
    setPageIndex(0);
  }, [chip]);

  const chipNames = useMemo(() => {
    const fromServer = counts?.filters.map((f) => f.filter) ?? [];
    return fromServer.length > 0 ? fromServer : CHIP_ORDER;
  }, [counts]);

  const chipCount = (name: string): number | null => counts?.filters.find((f) => f.filter === name)?.count ?? null;

  const sortedRows = useMemo(
    () =>
      [...rows].sort(
        (a, b) =>
          URGENCY_RANK[a.urgency] - URGENCY_RANK[b.urgency] ||
          new Date(a.settlementDeadline).getTime() - new Date(b.settlementDeadline).getTime()
      ),
    [rows]
  );

  const pageCount = Math.max(1, Math.ceil(sortedRows.length / PAGE_SIZE));
  const safePage = Math.min(pageIndex, pageCount - 1);
  const pageRows = sortedRows.slice(safePage * PAGE_SIZE, safePage * PAGE_SIZE + PAGE_SIZE);

  const countdownRows = useMemo(
    () =>
      countdown === null
        ? []
        : [...countdown.rows].sort(
            (a, b) =>
              URGENCY_RANK[a.urgency] - URGENCY_RANK[b.urgency] ||
              new Date(a.settlementDeadline).getTime() - new Date(b.settlementDeadline).getTime()
          ),
    [countdown]
  );

  const overdueOrCriticalCount = countdownRows.filter(
    (r) => r.urgency === "Overdue" || r.urgency === "Critical"
  ).length;

  const pendingRemittanceTotal = remittances?.reduce((sum, r) => sum + r.amount, 0) ?? 0;

  function openPolicy(policyId: string, policyNumber: string) {
    openTab({
      navType: "policy-file",
      page: "policy-file",
      kind: "multi-record",
      recordId: policyId,
      title: policyNumber,
      payload: { policyId },
    });
  }

  function openSingleton(navType: string, page: Parameters<typeof openTab>[0]["page"], title: string) {
    openTab({ navType, page, kind: "singleton", title });
  }

  /** The export endpoint needs the bearer token, so this cannot be a plain <a href>: the browser
   * would send no Authorization header and get a 401 page instead of a file. */
  async function downloadExport() {
    setExporting(true);
    setExportError(null);
    try {
      const headers: Record<string, string> = { Authorization: `Bearer ${getToken() ?? ""}` };
      const orgId = getActiveOrgId();
      if (orgId) headers["X-Organization-Id"] = orgId;

      const response = await fetch(`/api/installments/export?filter=${encodeURIComponent(chip)}`, { headers });

      if (response.status === 401) {
        throw new ApiError(401, "نشست شما منقضی شده است.");
      }
      if (!response.ok) {
        // The endpoint explains itself in the body — an unknown chip is a 400 with a Persian title.
        // Swallowing that and reporting a generic failure would hide the one sentence that says why.
        const problem = (await response.json().catch(() => null)) as { title?: string } | null;
        throw new ApiError(response.status, problem?.title ?? "تهیهٔ خروجی اکسل ناموفق بود.");
      }

      const blob = await response.blob();
      const url = URL.createObjectURL(blob);
      const anchor = document.createElement("a");
      anchor.href = url;
      anchor.download = `aqsat-installments-${new Date().toISOString().slice(0, 10)}.xlsx`;
      anchor.click();
      URL.revokeObjectURL(url);
    } catch (err) {
      setExportError(err instanceof ApiError ? err.message : "تهیهٔ خروجی اکسل ناموفق بود.");
    } finally {
      setExporting(false);
    }
  }

  function startPayment(row: WorklistRowDto) {
    setPaying(row);
    document.getElementById(RECORD_PAYMENT_PANEL_ID)?.scrollIntoView({ behavior: "smooth", block: "nearest" });
  }

  // The first paint is the skeleton, not a half-drawn page: a dashboard that renders zeroes while
  // it is still fetching teaches the agent to distrust every figure on it.
  if (dash === null && dashError === null) {
    return (
      <div aria-busy>
        <SkeletonRail />
        <div className="mt-4">
          <SkeletonRows rows={6} cols={4} />
        </div>
      </div>
    );
  }

  if (dash === null) {
    return <ErrorState title="ارقام امروز بارگذاری نشد" description={dashError ?? undefined} onRetry={load} />;
  }

  const figures = dash.figures;
  const goal = dash.goal;
  const trend: GroupedTrendDay[] = dash.trend.map((t) => ({
    date: t.date,
    collected: t.collected,
    goalRemaining: t.goalRemaining,
  }));
  const hasGoal = goal.dailyTarget != null;

  return (
    <div>
      <div className="mb-3.5 flex flex-wrap items-end justify-between gap-3">
        <div>
          <h2 className="text-xl font-extrabold tracking-tight text-(--ice)">
            امروز <em className="font-extralight not-italic text-(--ice-2)">در یک نگاه</em>
          </h2>
          <div className="mt-0.5 text-[12.5px] text-(--ice-3)">
            آخرین همگام‌سازی با فناوران: {agoLabel(dash.sync.lastSyncAgoMinutes)}
            {dash.sync.openMismatchCount > 0 && (
              <>
                {" · "}
                <span className="text-(--amber)">{fa(dash.sync.openMismatchCount)} ردیف مغایرت باز</span>
              </>
            )}
          </div>
        </div>

        <div className="flex flex-wrap items-center gap-2">
          <button type="button" onClick={downloadExport} disabled={exporting} className={BTN_SECONDARY_SM}>
            <span className="inline-flex items-center gap-1.5">
              <DownloadSimpleIcon size={14} />
              {exporting ? "در حال تهیه…" : "خروجی اکسل"}
            </span>
          </button>
          <button
            type="button"
            onClick={() => openSingleton("import-fanavaran", "import-fanavaran", "آپلود فایل فناوران")}
            className={BTN_SECONDARY_SM}
          >
            <span className="inline-flex items-center gap-1.5">
              <ArrowsClockwiseIcon size={14} />
              همگام‌سازی
            </span>
          </button>
          <button
            type="button"
            onClick={() => {
              if (sortedRows.length > 0) startPayment(sortedRows[0]);
            }}
            disabled={sortedRows.length === 0}
            title={sortedRows.length === 0 ? "قسطی برای دریافت وجود ندارد" : undefined}
            className={BTN_PRIMARY_SM}
          >
            <span className="inline-flex items-center gap-1.5">
              <PlusCircleIcon size={14} />
              ثبت دریافت
            </span>
          </button>
        </div>
      </div>

      {exportError && (
        <div className="mb-3 rounded-(--r) border border-(--ember)/30 bg-(--ember)/10 px-3.5 py-2.5 text-[12.5px] text-(--ember)">
          {exportError}
        </div>
      )}

      {failedPanels.length > 0 && (
        <div className="mb-3.5 flex flex-wrap items-center justify-between gap-3 rounded-(--r) border border-(--ember)/30 bg-(--ember)/10 px-3.5 py-2.5">
          <div className="text-[12.5px] text-(--ember)">
            بارگذاری این بخش‌ها ناموفق بود: {failedPanels.join("، ")}
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

      <div className="mb-3.5 grid grid-cols-2 overflow-hidden rounded-(--r-lg) border border-(--edge) bg-(--pane) lg:grid-cols-4">
        <Fig
          label="وصول امروز"
          value={money(figures.collectedToday)}
          caption={
            figures.collectedTodayDeltaPct === null
              ? "هفت روز قبل پرداختی ثبت نشده"
              : `${fa(Math.abs(figures.collectedTodayDeltaPct))}٪ ${
                  figures.collectedTodayDeltaPct >= 0 ? "بیشتر" : "کمتر"
                } از میانگین هفته`
          }
          tone={figures.collectedTodayDeltaPct !== null && figures.collectedTodayDeltaPct < 0 ? "amber" : "moss"}
          sparkline={figures.collectedLast7Days}
        />
        <Fig
          label="سررسید امروز"
          value={fa(figures.dueTodayCount)}
          caption={`${fa(figures.dueTodayUnpaidCount)} پرداخت‌نشده · ${fa(figures.dueTodaySettledCount)} تسویه‌شده`}
          tone={figures.dueTodayUnpaidCount > 0 ? "amber" : undefined}
        />
        <Fig
          label="معوق بیش از ۳۰ روز"
          value={money(figures.overdue30Amount)}
          caption={`${fa(figures.overdue30Count)} قسط`}
          tone={figures.overdue30Count > 0 ? "ember" : undefined}
        />
        <Fig
          label="چک در جریان"
          value={fa(figures.chequesInFlightCount)}
          caption={
            figures.chequesBouncedCount > 0 ? `${fa(figures.chequesBouncedCount)} چک برگشتی` : "چک برگشتی ثبت نشده"
          }
          tone={figures.chequesBouncedCount > 0 ? "ember" : undefined}
        />
      </div>

      <GoalStrip goal={goal} onSetGoal={() => openSingleton("settings-agency", "agency-settings", "مشخصات نمایندگی")} />

      <div className="grid grid-cols-1 gap-4 xl:grid-cols-[minmax(0,1.9fr)_minmax(0,1fr)]">
        <Panel
          title="اقساط نیازمند اقدام"
          aside={counts ? `${fa(counts.total)} قسط · ${money(counts.totalBalance)}` : undefined}
          error={rowsError}
          onRetry={load}
          loading={rowsLoading && rows.length === 0 && rowsError === null}
        >
          <div className="mb-2.5 flex flex-wrap items-center gap-1.5">
            {chipNames.map((name) => {
              const value = chipCount(name);
              const active = chip === name;
              return (
                <button
                  key={name}
                  type="button"
                  onClick={() => setChip(name)}
                  className={`rounded-full border px-2.5 py-1 text-[11.5px] transition-colors ${
                    active
                      ? "border-(--mint) bg-(--mint)/12 font-semibold text-(--mint)"
                      : "border-(--edge-2) text-(--ice-3) hover:bg-(--hov) hover:text-(--ice)"
                  }`}
                >
                  {CHIP_LABEL[name] ?? name}
                  {value !== null && <span className="ms-1.5 tabular-nums">{fa(value)}</span>}
                </button>
              );
            })}
          </div>

          {rowsError ? null : sortedRows.length === 0 ? (
            <InlineEmpty
              title={chip === "all" ? "اقساطی نیازمند اقدام نیست" : "در این دسته قسطی نیست"}
              description={
                chip === "all"
                  ? "در حال حاضر قسط سررسیدشده یا نیازمند پیگیری وجود ندارد."
                  : "برای دیدن همهٔ اقساط، چیپ «همه» را انتخاب کنید."
              }
            />
          ) : (
            <Table plain>
              <thead>
                <tr>
                  <Th>بیمه‌گذار</Th>
                  <Th>قسط</Th>
                  <Th>وضعیت</Th>
                  <Th>مهلت تسویه</Th>
                  <Th>مانده</Th>
                  <Th />
                </tr>
              </thead>
              <tbody>
                {pageRows.map((r) => (
                  <Tr
                    key={r.installmentId}
                    className="group/row"
                    onClick={() => openPolicy(r.policyId, r.policyNumber)}
                  >
                    <Td className="py-2.75">
                      <div className="font-semibold text-(--ice)">{r.customerFullName}</div>
                      <div className="mt-0.5 text-[11.5px] text-(--ice-3)" dir="ltr">
                        {r.customerMobile ? fa(r.customerMobile) : "بدون شمارهٔ تماس"}
                      </div>
                    </Td>
                    <Td className="py-2.75">
                      <div className="text-(--ice-2)">
                        {fa(r.seqNo)} <span className="text-(--ice-3)">از {fa(r.seqTotal)}</span>
                      </div>
                      <div className="mt-0.5 text-[11.5px] text-(--ice-3)">بیمه‌نامهٔ {fa(r.policyNumber)}</div>
                    </Td>
                    <Td className="py-2.75">
                      <StatusBadge tone={URGENCY_TONE[r.urgency]}>{URGENCY_LABEL[r.urgency]}</StatusBadge>
                    </Td>
                    <Td className="py-2.75">
                      <div className="text-(--ice-2)">{toJalaliDisplay(r.settlementDeadline)}</div>
                      <div className="mt-0.5 text-[11.5px] text-(--ice-3)">{daysLabel(r)}</div>
                    </Td>
                    <Td className="py-2.75 font-bold tabular-nums">{money(r.balance)}</Td>
                    <Td className="py-2.75">
                      <div className="flex justify-end gap-1.5 opacity-0 transition-opacity focus-within:opacity-100 group-hover/row:opacity-100">
                        {canWrite && (
                          <button
                            type="button"
                            onClick={(e) => {
                              e.stopPropagation();
                              setLoggingContact(r);
                            }}
                            className="rounded-(--r) border border-(--edge-2) px-2 py-1 text-[11.5px] text-(--ice-2) transition-colors hover:bg-(--btn-hov) hover:text-(--ice)"
                          >
                            <span className="inline-flex items-center gap-1">
                              <PhoneIcon size={12} />
                              تماس
                            </span>
                          </button>
                        )}
                        <button
                          type="button"
                          onClick={(e) => {
                            e.stopPropagation();
                            startPayment(r);
                          }}
                          className="rounded-(--r) border border-(--mint) px-2 py-1 text-[11.5px] font-semibold text-(--mint) transition-colors hover:bg-(--mint) hover:text-(--on-mint)"
                        >
                          دریافت
                        </button>
                      </div>
                    </Td>
                  </Tr>
                ))}
              </tbody>
              <tfoot>
                <tr className="border-t border-(--edge)">
                  <td colSpan={6} className="px-3 py-2 text-[11.5px] text-(--ice-3)">
                    <div className="flex flex-wrap items-center justify-between gap-2">
                      <span>
                        نمایش {fa(safePage * PAGE_SIZE + 1)} تا{" "}
                        {fa(Math.min((safePage + 1) * PAGE_SIZE, sortedRows.length))} از {fa(sortedRows.length)}
                        {sortedRows.length >= 300 && " (سقف ۳۰۰ ردیف)"}
                      </span>
                      {pageCount > 1 && (
                        <span className="flex items-center gap-1.5">
                          <button
                            type="button"
                            onClick={() => setPageIndex(Math.max(0, safePage - 1))}
                            disabled={safePage === 0}
                            className="rounded-(--r) border border-(--edge-2) px-2 py-0.5 transition-colors hover:bg-(--hov) disabled:opacity-40"
                          >
                            قبلی
                          </button>
                          <span className="tabular-nums">
                            {fa(safePage + 1)} / {fa(pageCount)}
                          </span>
                          <button
                            type="button"
                            onClick={() => setPageIndex(Math.min(pageCount - 1, safePage + 1))}
                            disabled={safePage >= pageCount - 1}
                            className="rounded-(--r) border border-(--edge-2) px-2 py-0.5 transition-colors hover:bg-(--hov) disabled:opacity-40"
                          >
                            بعدی
                          </button>
                        </span>
                      )}
                    </div>
                  </td>
                </tr>
              </tfoot>
            </Table>
          )}

          {/* The payment panel sits directly under the list, so the row the agent clicked stays on
              screen while they fill the form in. */}
          {paying && (
            <div className="mt-3.5">
              <RecordPaymentDialog
                variant="inline"
                installmentId={paying.installmentId}
                customerFullName={`${paying.customerFullName} — قسط ${fa(paying.seqNo)} از ${fa(paying.seqTotal)}`}
                suggestedAmount={paying.balance}
                onClose={() => setPaying(null)}
                onRecorded={load}
              />
            </div>
          )}
        </Panel>

        <div className="flex min-w-0 flex-col gap-4">
          <Panel
            title="شمارش‌معکوس تسویه"
            aside={countdown === null ? undefined : `${fa(countdownRows.length)} قسط`}
            error={countdownFailed ? "آخرین به‌روزرسانی این بخش ناموفق بود؛ ارقام زیر ممکن است قدیمی باشند." : null}
            onRetry={load}
          >
            {countdown === null ? (
              <InlineEmpty title="شمارش‌معکوس در دسترس نیست" description="این بخش بارگذاری نشد." />
            ) : (
              <>
                <div className="mb-2.5 grid grid-cols-3 gap-2">
                  <MiniStat label="بدهی به بیمه‌گر" value={money(countdown.owed)} />
                  <MiniStat label="وصول‌شده" value={money(countdown.collected)} tone="moss" />
                  <MiniStat
                    label="کسری"
                    value={money(countdown.shortfall)}
                    tone={countdown.shortfall > 0 ? "ember" : undefined}
                  />
                </div>

                {overdueOrCriticalCount > 0 && (
                  <div className="mb-2.5 rounded-(--r) border border-(--ember)/30 bg-(--ember)/10 px-2.5 py-1.5 text-[11.5px] leading-relaxed text-(--ember)">
                    {fa(overdueOrCriticalCount)} قسط معوق یا بحرانی — این‌ها اولین چیزی هستند که باید تسویه شوند،
                    وگرنه سامانهٔ صدور بیمه‌گر قفل می‌شود.
                  </div>
                )}

                {countdownRows.length === 0 ? (
                  <div className="py-3 text-center text-[12.5px] text-(--ice-3)">
                    هیچ قسطی در پنجرهٔ فعال تسویه نیست.
                  </div>
                ) : (
                  <div className="space-y-1.5">
                    {(showAllCountdown ? countdownRows : countdownRows.slice(0, COUNTDOWN_PREVIEW)).map((r) => (
                      <button
                        key={r.installmentId}
                        type="button"
                        onClick={() => openPolicy(r.policyId, r.policyNumber)}
                        className="flex w-full items-center justify-between gap-2 rounded-(--r) border border-(--edge) px-2.5 py-2 text-start transition-colors hover:bg-(--hov)"
                      >
                        <span className="min-w-0">
                          <span className="block truncate text-[12.5px] font-semibold text-(--ice)">
                            {r.customerFullName}
                          </span>
                          <span className="block text-[11px] text-(--ice-3)">
                            قسط {fa(r.seqNo)} · {daysLabel(r)}
                          </span>
                        </span>
                        <span className="flex flex-none items-center gap-2">
                          <span className="text-[12.5px] font-bold tabular-nums text-(--ice)">{money(r.balance)}</span>
                          <StatusBadge tone={URGENCY_TONE[r.urgency]}>{URGENCY_LABEL[r.urgency]}</StatusBadge>
                        </span>
                      </button>
                    ))}
                    {countdownRows.length > COUNTDOWN_PREVIEW && (
                      <button
                        type="button"
                        onClick={() => setShowAllCountdown((v) => !v)}
                        className="w-full rounded-(--r) px-2 py-1 text-[11.5px] font-semibold text-(--mint) transition-colors hover:bg-(--hov)"
                      >
                        {showAllCountdown ? "نمایش کمتر" : `نمایش همهٔ ${fa(countdownRows.length)} قسط`}
                      </button>
                    )}
                  </div>
                )}
              </>
            )}
          </Panel>

          <Panel title="روند وصول ۱۴ روز گذشته" aside={hasGoal ? "در برابر هدف روز" : "بدون هدف ماهانه"}>
            <GroupedTrendBars data={trend} hasGoal={hasGoal} height={190} />
          </Panel>

          <Panel
            title="یادآوری‌های پیامکی"
            aside={dash.activity.length > 0 ? `${fa(dash.activity.length)} رویداد` : undefined}
          >
            {dash.activity.length === 0 ? (
              <div className="py-3 text-center text-[12.5px] text-(--ice-3)">امروز رویداد پیامکی ثبت نشده است.</div>
            ) : (
              <ul className="space-y-1.5">
                {dash.activity.map((a, i) => (
                  <li key={`${a.kind}-${i}`} className="flex items-start gap-2 text-[12.5px]">
                    <span
                      className={`mt-1.5 inline-block h-1.5 w-1.5 flex-none rounded-full ${
                        a.tone === "ok" ? "bg-(--moss)" : a.tone === "warn" ? "bg-(--amber)" : "bg-(--ember)"
                      }`}
                    />
                    <span className="min-w-0">
                      <span className="block text-(--ice)">{a.title}</span>
                      <span className="block text-[11px] text-(--ice-3)">{a.detail}</span>
                    </span>
                  </li>
                ))}
              </ul>
            )}
          </Panel>

          <AttentionPanel
            incompleteProfiles={incompleteProfiles}
            dueRenewals={dueRenewals}
            onOpenCustomers={() => openSingleton("customer-completion", "customer-completion", "تکمیل پروندهٔ مشتریان")}
            onOpenRenewals={() => openSingleton("policies-renewal", "renewal-watches", "سررسید تمدید")}
          />

          {canSeeFinance && (
            <Panel title="سود و زیان این ماه">
              {pnl === null ? (
                <InlineEmpty title="سود و زیان در دسترس نیست" description="این بخش بارگذاری نشد." />
              ) : (
                <div className="text-[12.5px]">
                  <div className="flex items-center justify-between">
                    <span className="text-(--ice-3)">درآمد</span>
                    <b className="tabular-nums text-(--ice)">{money(pnl.totalIncome)}</b>
                  </div>
                  <div className="mt-1 flex items-center justify-between">
                    <span className="text-(--ice-3)">هزینه</span>
                    <b className="tabular-nums text-(--ice)">{money(pnl.totalExpense)}</b>
                  </div>
                  <div className="mt-2 flex items-center justify-between border-t border-(--edge) pt-2">
                    <span className="font-semibold text-(--ice-2)">سود خالص</span>
                    <b
                      className={`text-[15px] font-extrabold tabular-nums ${
                        pnl.netProfit >= 0 ? "text-(--moss)" : "text-(--ember)"
                      }`}
                    >
                      {money(pnl.netProfit)}
                    </b>
                  </div>
                  <button
                    type="button"
                    onClick={() => openSingleton("reports-pnl", "pnl", "سود و زیان")}
                    className="mt-2 w-full rounded-(--r) px-2 py-1 text-[11.5px] font-semibold text-(--mint) transition-colors hover:bg-(--hov)"
                  >
                    مشاهدهٔ گزارش کامل
                  </button>
                </div>
              )}
            </Panel>
          )}

          <Panel
            title="تسویه با بیمه‌گر"
            aside={
              missingSerials && missingSerials.missingCount > 0
                ? `${fa(missingSerials.missingCount)} سریال جا افتاده`
                : undefined
            }
          >
            {!canSeeFinance ? (
              <div className="py-3 text-center text-[12.5px] text-(--ice-3)">
                برای دیدن این بخش دسترسی مالی لازم است.
              </div>
            ) : remittances === null ? (
              <InlineEmpty title="تسویه با بیمه‌گر در دسترس نیست" description="این بخش بارگذاری نشد." />
            ) : (
              <>
                {remittances.length === 0 ? (
                  <div className="py-3 text-center text-[12.5px] text-(--ice-3)">
                    قسط تسویه‌شده‌ای بدون تسویه با بیمه‌گر نمانده است.
                  </div>
                ) : (
                  <div className="text-[12.5px] text-(--ice-2)">
                    <div className="flex items-center justify-between">
                      <span className="text-(--ice-3)">قسط تسویه‌شدهٔ بی‌سابقه</span>
                      <b className="tabular-nums text-(--ice)">{fa(remittances.length)}</b>
                    </div>
                    <div className="mt-1 flex items-center justify-between">
                      <span className="text-(--ice-3)">جمع</span>
                      <b className="font-bold tabular-nums text-(--amber)">{money(pendingRemittanceTotal)}</b>
                    </div>
                  </div>
                )}
                <button
                  type="button"
                  onClick={() =>
                    openSingleton("cash-flow-insurer-remittance", "insurer-remittance", "پرداخت به بیمه‌گر")
                  }
                  className="mt-2.5 w-full rounded-(--r) px-2 py-1 text-[11.5px] font-semibold text-(--mint) transition-colors hover:bg-(--hov)"
                >
                  رفتن به تسویه با بیمه‌گر
                </button>
              </>
            )}
          </Panel>

          <Panel title="بازبینی دستی اعتبار">
            {reviews === null || reviews.length === 0 ? (
              <InlineEmpty
                title="پروندهٔ در انتظار بازبینی نیست"
                description="هر پرونده‌ای که قوانین اعتبارسنجی متوقف کنند، همین‌جا ظاهر می‌شود."
              />
            ) : (
              <ul className="space-y-1.5">
                {reviews.slice(0, 4).map((r) => (
                  <li key={r.id} className="flex items-center justify-between gap-2 text-[12.5px]">
                    <span className="min-w-0 truncate text-(--ice)">{r.customerName}</span>
                    <span className="flex flex-none items-center gap-2">
                      <span className="tabular-nums text-(--ice-3)">امتیاز {fa(r.score)}</span>
                      <StatusBadge tone="amber">{r.statusFa}</StatusBadge>
                    </span>
                  </li>
                ))}
                <li>
                  <button
                    type="button"
                    onClick={() => openSingleton("risk-manual-reviews", "risk-manual-reviews", "بررسی‌های دستی")}
                    className="w-full rounded-(--r) px-2 py-1 text-[11.5px] font-semibold text-(--mint) transition-colors hover:bg-(--hov)"
                  >
                    مشاهدهٔ همهٔ {fa(reviews.length)} پرونده
                  </button>
                </li>
              </ul>
            )}
          </Panel>
        </div>
      </div>

      <div className="mt-4 text-[11.5px] leading-relaxed text-(--ice-3)">
        این تب سنجاق شده و بسته نمی‌شود. کلیک روی هر ردیف، بیمه‌نامه را در تب جدید باز می‌کند و این تب
        دست‌نخورده می‌ماند.
      </div>

      {loggingContact && (
        <LogContactDialog
          policyId={loggingContact.policyId}
          installmentId={loggingContact.installmentId}
          customerFullName={`${loggingContact.customerFullName} — قسط ${fa(loggingContact.seqNo)}`}
          suggestedAmount={loggingContact.balance}
          onClose={() => setLoggingContact(null)}
          onRecorded={load}
        />
      )}
    </div>
  );
}

/** A bordered section of the page. Every panel owns its own loading / empty / error state rather
 * than letting the page render a blank box (rules 15/16). */
function Panel({
  title,
  aside,
  error,
  onRetry,
  loading,
  children,
}: {
  title: string;
  aside?: string;
  error?: string | null;
  onRetry?: () => void;
  loading?: boolean;
  children: React.ReactNode;
}) {
  return (
    <section className="rounded-(--r-lg) border border-(--edge) bg-(--pane) p-3.5">
      <div className="mb-2.5 flex items-center justify-between gap-2">
        <h3 className="text-[13px] font-bold text-(--ice)">{title}</h3>
        {aside && <span className="text-[11.5px] text-(--ice-3)">{aside}</span>}
      </div>

      {error ? (
        <div className="rounded-(--r) border border-(--ember)/30 bg-(--ember)/10 px-2.5 py-2 text-[12px] text-(--ember)">
          <div>{error}</div>
          {onRetry && (
            <button
              type="button"
              onClick={onRetry}
              className="mt-1.5 rounded-(--r) border border-(--ember)/45 px-2 py-0.5 text-[11.5px] font-semibold transition-colors hover:bg-(--ember)/15"
            >
              بازخوانی
            </button>
          )}
        </div>
      ) : loading ? (
        <div aria-busy>
          <SkeletonBlock className="mb-2 h-3 w-24" />
          <SkeletonBlock className="h-3 w-40" />
        </div>
      ) : (
        children
      )}
    </section>
  );
}

/** The in-panel empty state: dashed and quiet, because it sits inside a panel that already draws
 * the frame — a second bordered card inside the first would read as a box in a box. */
function InlineEmpty({ title, description }: { title: string; description: string }) {
  return (
    <div className="grid place-items-center rounded-(--r) border border-dashed border-(--edge-2) px-3 py-5 text-center">
      <span className="text-[12.5px] text-(--ice-2)">{title}</span>
      <span className="mt-1 text-[11px] text-(--ice-3)">{description}</span>
    </div>
  );
}

function GoalStrip({ goal, onSetGoal }: { goal: TodayGoalDto; onSetGoal: () => void }) {
  if (goal.monthlyGoal === null) {
    return (
      <div className="mb-3.5 flex flex-wrap items-center justify-between gap-2 rounded-(--r-lg) border border-dashed border-(--edge-2) px-3.5 py-2.5">
        <span className="text-[12.5px] text-(--ice-2)">
          هدف وصول ماهانه تعیین نشده — تا وقتی هدفی نباشد، نمودار فقط وصول واقعی را نشان می‌دهد.
        </span>
        <button
          type="button"
          onClick={onSetGoal}
          className="rounded-(--r) px-2 py-1 text-[11.5px] font-semibold text-(--mint) transition-colors hover:bg-(--hov)"
        >
          تعیین هدف
        </button>
      </div>
    );
  }

  const percent = Math.max(0, Math.min(100, Math.round(goal.monthProgressPct ?? 0)));

  return (
    <div className="mb-3.5 rounded-(--r-lg) border border-(--edge) bg-(--pane) px-3.5 py-2.5">
      <div className="mb-1.5 flex flex-wrap items-center justify-between gap-2 text-[12.5px]">
        <span className="text-(--ice-2)">
          هدف ماه: <b className="font-bold text-(--ice)">{money(goal.monthlyGoal)}</b>
          {goal.dailyTarget !== null && <span className="text-(--ice-3)"> · هدف امروز {money(goal.dailyTarget)}</span>}
        </span>
        <span className="text-(--ice-2)">
          وصول‌شده {money(goal.monthCollected)}{" "}
          <b className="font-bold tabular-nums text-(--moss)">{fa(percent)}٪</b>
        </span>
      </div>
      <div className="h-1 overflow-hidden rounded-full bg-(--edge)">
        <div
          className="h-full rounded-full bg-(--moss) transition-[width] duration-500"
          style={{ width: `${percent}%` }}
        />
      </div>
    </div>
  );
}

function AttentionPanel({
  incompleteProfiles,
  dueRenewals,
  onOpenCustomers,
  onOpenRenewals,
}: {
  incompleteProfiles: IncompleteProfileSummaryDto | null;
  dueRenewals: RenewalWatchDto[] | null;
  onOpenCustomers: () => void;
  onOpenRenewals: () => void;
}) {
  const profileCount = incompleteProfiles?.total ?? 0;
  const renewalCount = dueRenewals?.length ?? 0;

  if (profileCount === 0 && renewalCount === 0) {
    return (
      <Panel title="نیازمند توجه">
        <InlineEmpty
          title="موردی برای پیگیری نیست"
          description="پروندهٔ ناقص یا بیمه‌نامهٔ در آستانهٔ انقضا وجود ندارد."
        />
      </Panel>
    );
  }

  return (
    <Panel title="نیازمند توجه" aside={`${fa((profileCount > 0 ? 1 : 0) + (renewalCount > 0 ? 1 : 0))} مورد`}>
      <div className="space-y-2.5">
        {profileCount > 0 && incompleteProfiles && (
          <AdvisoryRow
            icon={UserListIcon}
            title={`${fa(profileCount)} مشتری اطلاعات ناقص دارند`}
            action="تکمیل پرونده‌ها"
            onAction={onOpenCustomers}
          >
            <div className="flex flex-wrap gap-x-3 gap-y-1 text-[11.5px]">
              <Gap label="بدون موبایل" count={incompleteProfiles.withoutMobile} />
              <Gap label="بدون کد ملی" count={incompleteProfiles.withoutNationalId} />
              <Gap label="بدون آدرس" count={incompleteProfiles.withoutAddress} />
              <Gap label="بدون کد پستی" count={incompleteProfiles.withoutPostalCode} />
              <Gap label="بدون نام" count={incompleteProfiles.withoutName} />
            </div>
          </AdvisoryRow>
        )}

        {renewalCount > 0 && dueRenewals && (
          <AdvisoryRow
            icon={AlarmIcon}
            title={`${fa(renewalCount)} بیمه‌نامه در پنجرهٔ تمدید`}
            action="مشاهدهٔ همه"
            onAction={onOpenRenewals}
          >
            <div className="text-[11.5px] leading-relaxed text-(--ice-3)">
              نزدیک‌ترین انقضا {toJalaliDisplay(dueRenewals[0].currentExpiryDate)}
              {"؛ "}
              {dueRenewals
                .slice(0, 3)
                .map((w) => w.customerFullName ?? w.prospectName ?? "بدون نام")
                .join("، ")}
              {renewalCount > 3 && ` و ${fa(renewalCount - 3)} مورد دیگر`}
            </div>
          </AdvisoryRow>
        )}
      </div>
    </Panel>
  );
}

function AdvisoryRow({
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
    <div className="rounded-(--r) border border-(--edge) p-2.5">
      <div className="mb-1.5 flex items-start gap-2">
        <Icon size={15} className="mt-0.5 flex-none text-(--ice-2)" />
        <span className="min-w-0 flex-1 text-[12.5px] font-semibold text-(--ice)">{title}</span>
      </div>
      {children}
      <button
        type="button"
        onClick={onAction}
        className="mt-1.5 inline-flex items-center gap-1 rounded-(--r) px-1.5 py-0.5 text-[11.5px] font-semibold text-(--mint) transition-colors hover:bg-(--hov)"
      >
        <ArrowSquareOutIcon size={12} />
        {action}
      </button>
    </div>
  );
}

function Gap({ label, count }: { label: string; count: number }) {
  return (
    <span>
      <span className="text-(--ice-3)">{label} </span>
      <b className={count > 0 ? "font-bold tabular-nums text-(--amber)" : "font-bold tabular-nums text-(--ice-2)"}>
        {fa(count)}
      </b>
    </span>
  );
}

function MiniStat({ label, value, tone }: { label: string; value: string; tone?: "ember" | "moss" }) {
  const color = tone === "ember" ? "text-(--ember)" : tone === "moss" ? "text-(--moss)" : "text-(--ice)";
  return (
    <div className="rounded-(--r) border border-(--edge) px-2 py-1.5">
      <div className="text-[10.5px] text-(--ice-3)">{label}</div>
      <div className={`mt-0.5 text-[13px] font-extrabold tabular-nums ${color}`}>{value}</div>
    </div>
  );
}

/** A seven-point sparkline for the collected-today figure. Inline SVG, no dependency — the values
 * are already in the dashboard payload. */
function Sparkline({ values }: { values: number[] }) {
  if (values.length < 2) return null;

  const w = 66;
  const h = 20;
  const max = Math.max(...values, 1);
  const step = w / (values.length - 1);
  const points = values.map((v, i) => `${(i * step).toFixed(1)},${(h - (v / max) * (h - 3)).toFixed(1)}`).join(" ");

  return (
    <svg width={w} height={h} viewBox={`0 0 ${w} ${h}`} aria-hidden className="text-(--mint)">
      <polyline
        points={points}
        fill="none"
        stroke="currentColor"
        strokeWidth={1.5}
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  );
}

function Fig({
  label,
  value,
  caption,
  tone,
  sparkline,
}: {
  label: string;
  value: string;
  caption: string;
  tone?: "ember" | "amber" | "moss";
  sparkline?: number[];
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
      <div className="flex items-end justify-between gap-2">
        <div className={`text-[19px] font-extrabold tracking-tight tabular-nums ${valueColor}`}>{value}</div>
        {sparkline && sparkline.length > 1 && <Sparkline values={sparkline} />}
      </div>
      <div className="text-[11.5px] text-(--ice-3)">{caption}</div>
    </div>
  );
}
