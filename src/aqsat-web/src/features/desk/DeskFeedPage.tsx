import { useCallback, useEffect, useState } from "react";
import { useTabsStore } from "../../app/store/tabsStore";
import { useTabKey } from "../shell/TabContext";
import { useLiveReload } from "../shell/useLiveReload";
import { api, ApiError } from "../../lib/api";
import { fa, money } from "../../lib/persian";
import { toJalaliDisplay, toJalaliDateTimeDisplay } from "../../lib/jalali";
import { EmptyState } from "../../components/EmptyState";
import { Table, Td, Th, Tr } from "../../components/Table";
import { RecordPaymentDialog } from "../today/RecordPaymentDialog";

type Mode = "reminders" | "overdue" | "notifications";

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
  rows: CountdownRowDto[];
}

interface ReminderLogDto {
  id: string;
  policyNumber: string | null;
  seqNo: number | null;
  recipientType: string;
  mobile: string;
  offsetDays: number;
  status: string;
  sentAt: string;
}

interface RiskWarningDto {
  id: string;
  customerId: string;
  customerName: string;
  typeFa: string;
  message: string;
  isRead: boolean;
  createdAt: string;
}

const TITLE: Record<Mode, { heading: string; sub: string; empty: string; emptyIcon: string; emptyDesc: string }> = {
  reminders: {
    heading: "یادآوری‌های امروز",
    sub: "اقساطی که امروز سررسید می‌شوند",
    empty: "امروز قسطی سررسید نمی‌شود.",
    emptyIcon: "📅",
    emptyDesc: "قسطی برای یادآوری امروز نیست — فردا دوباره بررسی کنید.",
  },
  overdue: {
    heading: "کارهای معوق",
    sub: "اقساطی که از مهلت تسویه گذشته‌اند",
    empty: "هیچ قسط معوقی نیست.",
    emptyIcon: "✅",
    emptyDesc: "همهٔ اقساط در مهلت تسویهٔ خود قرار دارند.",
  },
  notifications: {
    heading: "اعلان‌ها",
    sub: "معوق/بحرانی + هشدارهای ریسک + آخرین پیامک‌های ارسالی",
    empty: "اعلان تازه‌ای نیست.",
    emptyIcon: "🔔",
    emptyDesc: "هیچ قسط معوق یا بحرانی وجود ندارد، هشدار ریسکی نخوانده مانده و پیامک تازه‌ای ارسال نشده است.",
  },
};

const URGENCY_LABEL: Record<CountdownRowDto["urgency"], string> = {
  Overdue: "معوق",
  Critical: "بحرانی",
  Warning: "هشدار",
  Upcoming: "سررسید نزدیک",
  Future: "آینده",
};

function todayIso(): string {
  const now = new Date();
  return new Date(now.getFullYear(), now.getMonth(), now.getDate()).toISOString().slice(0, 10);
}

export function DeskFeedPage() {
  const tabKey = useTabKey();
  const tab = useTabsStore((s) => s.tabs.find((t) => t.key === tabKey));
  const openTab = useTabsStore((s) => s.openTab);
  const mode = ((tab?.payload as { mode?: Mode } | undefined)?.mode ?? "overdue") as Mode;

  const [rows, setRows] = useState<CountdownRowDto[] | null>(null);
  const [log, setLog] = useState<ReminderLogDto[] | null>(null);
  const [warnings, setWarnings] = useState<RiskWarningDto[] | null>(null);
  const [warningsError, setWarningsError] = useState<string | null>(null);
  const [payingRow, setPayingRow] = useState<CountdownRowDto | null>(null);
  const [error, setError] = useState<string | null>(null);

  const reload = useCallback(() => {
    api
      .get<CountdownDashboardDto>("/countdown")
      .then((d) => setRows(d.rows))
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری اطلاعات"));
    if (mode === "notifications") {
      api
        .get<ReminderLogDto[]>("/sms/log?take=20")
        .then(setLog)
        .catch(() => setLog([]));
      api
        .get<RiskWarningDto[]>("/risk/warnings?unreadOnly=true")
        .then((w) => {
          setWarnings(w.slice(0, 10));
          setWarningsError(null);
        })
        .catch((err) => {
          // A failed warnings fetch must read as an error, never as "no warnings" (rule 16).
          setWarnings([]);
          setWarningsError(err instanceof ApiError ? err.message : "خطا در بارگذاری هشدارهای ریسک");
        });
    }
  }, [mode]);

  useEffect(() => {
    reload();
  }, [reload]);

  useLiveReload(reload);

  const filtered =
    rows === null
      ? null
      : mode === "reminders"
        ? rows.filter((r) => r.dueDate === todayIso())
        : mode === "overdue"
          ? rows.filter((r) => r.urgency === "Overdue")
          : rows.filter((r) => r.urgency === "Overdue" || r.urgency === "Critical");

  const { heading, sub, empty, emptyIcon, emptyDesc } = TITLE[mode];

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">{heading}</h2>
      <div className="mb-4.5 text-[12.5px] text-(--ice-3)">{sub}</div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      {!error && filtered === null && <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>}

      {!error && filtered !== null && (
        <>
          <div className="mb-2 text-[11.5px] text-(--ice-3)">{fa(filtered.length)} مورد</div>
          {filtered.length === 0 ? (
            <EmptyState icon={emptyIcon} title={empty} description={emptyDesc} />
          ) : (
            <Table>
              <thead>
                <tr>
                  {["بیمه‌گذار", "وضعیت", "سررسید", "مانده", ""].map((h) => (
                    <Th key={h}>{h}</Th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {filtered.map((r) => (
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
                    <Td className="py-2.75 font-semibold">{r.customerFullName}</Td>
                    <Td className="py-2.75 text-(--ice-3)">{URGENCY_LABEL[r.urgency]}</Td>
                    <Td className="py-2.75">{toJalaliDisplay(r.dueDate)}</Td>
                    <Td className="py-2.75 font-bold">{money(r.balance)}</Td>
                    <Td className="py-2.75">
                      <button
                        type="button"
                        onClick={(e) => {
                          e.stopPropagation();
                          setPayingRow(r);
                        }}
                        className="rounded-[8px] border border-(--mint) bg-(--mint) px-2.5 py-1 text-[11.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105"
                      >
                        ثبت پرداخت
                      </button>
                    </Td>
                  </Tr>
                ))}
              </tbody>
            </Table>
          )}
        </>
      )}

      {mode === "notifications" && warnings !== null && (
        <div className="mt-4.5 overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
          <div className="flex items-center justify-between border-b border-(--edge) px-4 py-2.5">
            <span className="text-[12.5px] font-semibold text-(--ice-2)">هشدارهای ریسک خوانده‌نشده</span>
            <button
              type="button"
              onClick={() =>
                openTab({
                  navType: "risk-warnings",
                  page: "risk-warnings",
                  kind: "singleton",
                  title: "هشدارها",
                })
              }
              className="text-[11.5px] font-semibold text-(--mint) underline"
            >
              مشاهدهٔ همه
            </button>
          </div>
          {warningsError ? (
            <div className="p-6 text-center text-[13.5px] text-(--ember)">{warningsError}</div>
          ) : warnings.length === 0 ? (
            <div className="p-6 text-center text-[13.5px] text-(--ice-3)">هشدار ریسک خوانده‌نشده‌ای نیست.</div>
          ) : (
            warnings.map((w) => (
              <button
                key={w.id}
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
                className="flex w-full items-center justify-between border-t border-(--edge) px-4 py-2 text-right text-[12.5px] transition-colors first:border-t-0 hover:bg-(--ice-1)/5"
              >
                <span>
                  ⚠ {w.customerName} — {w.message}
                </span>
                <span className="shrink-0 text-(--ice-3)">{toJalaliDateTimeDisplay(w.createdAt)}</span>
              </button>
            ))
          )}
        </div>
      )}

      {mode === "notifications" && log !== null && (
        <div className="mt-4.5 overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
          <div className="border-b border-(--edge) px-4 py-2.5 text-[12.5px] font-semibold text-(--ice-2)">آخرین پیامک‌های ارسالی</div>
          {log.length === 0 ? (
            <div className="p-6 text-center text-[13.5px] text-(--ice-3)">هنوز پیامکی ارسال نشده.</div>
          ) : (
            log.map((entry) => (
              <div key={entry.id} className="flex items-center justify-between border-t border-(--edge) px-4 py-2 text-[12.5px] first:border-t-0">
                <span>
                  {entry.policyNumber ?? "—"} {entry.seqNo ? `— قسط ${fa(entry.seqNo)}` : ""}
                </span>
                <span className="text-(--ice-3)">{entry.mobile}</span>
              </div>
            ))
          )}
        </div>
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
