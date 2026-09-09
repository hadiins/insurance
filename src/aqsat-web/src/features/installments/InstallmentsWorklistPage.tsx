import { useCallback, useEffect, useState } from "react";
import { useTabsStore } from "../../app/store/tabsStore";
import { useTabKey } from "../shell/TabContext";
import { useDraftState } from "../shell/useDraftState";
import { useLiveReload } from "../shell/useLiveReload";
import { api, ApiError } from "../../lib/api";
import { fa, money } from "../../lib/persian";
import { toJalaliDisplay } from "../../lib/jalali";
import { JalaliDateField } from "../../components/JalaliDateField";
import { EmptyState } from "../../components/EmptyState";
import { StatusBadge } from "../../components/StatusBadge";
import { Table, Td, Th, Tr } from "../../components/Table";
import { RecordPaymentDialog } from "../today/RecordPaymentDialog";
import { EditInstallmentDialog, type EditInstallmentTarget } from "./EditInstallmentDialog";

interface WorklistFilterPayload {
  overdueOnly?: boolean;
  status?: string;
}

interface InstallmentWorklistRowDto {
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
  urgency: "Overdue" | "Critical" | "Warning" | "Upcoming" | "Future";
}

interface InstallmentUrgencyCountDto {
  urgency: "Overdue" | "Critical" | "Warning" | "Upcoming" | "Future";
  count: number;
  balance: number;
}

interface InstallmentCountsDto {
  total: number;
  totalBalance: number;
  buckets: InstallmentUrgencyCountDto[];
}

const URGENCY_LABEL: Record<InstallmentWorklistRowDto["urgency"], string> = {
  Overdue: "معوق",
  Critical: "بحرانی",
  Warning: "هشدار",
  Upcoming: "سررسید نزدیک",
  Future: "آینده",
};

const URGENCY_TONE: Record<InstallmentWorklistRowDto["urgency"], "mint" | "ember" | "amber" | "neutral"> = {
  Overdue: "ember",
  Critical: "ember",
  Warning: "amber",
  Upcoming: "mint",
  Future: "neutral",
};

const URGENCY_CHIP_ORDER: InstallmentWorklistRowDto["urgency"][] = ["Overdue", "Critical", "Warning", "Upcoming", "Future"];

const STATUS_OPTIONS: { value: string; label: string }[] = [
  { value: "", label: "همهٔ وضعیت‌ها" },
  { value: "Unpaid", label: "پرداخت‌نشده" },
  { value: "Partial", label: "پرداخت جزئی (تسویه‌های جزئی)" },
  { value: "Settled", label: "تسویه‌شده" },
];

interface WorklistFilters {
  urgency: string;
  status: string;
  from: string;
  to: string;
  search: string;
}

export function InstallmentsWorklistPage() {
  const tabKey = useTabKey();
  const tab = useTabsStore((s) => s.tabs.find((t) => t.key === tabKey));
  const openTab = useTabsStore((s) => s.openTab);
  const initial = (tab?.payload as WorklistFilterPayload | undefined) ?? {};

  const [rows, setRows] = useState<InstallmentWorklistRowDto[] | null>(null);
  const [counts, setCounts] = useState<InstallmentCountsDto | null>(null);
  const [payingRow, setPayingRow] = useState<InstallmentWorklistRowDto | null>(null);
  const [editingRow, setEditingRow] = useState<EditInstallmentTarget | null>(null);
  const [error, setError] = useState<string | null>(null);

  const [filters, setFilters] = useDraftState<WorklistFilters>("worklist-filters", {
    urgency: initial.overdueOnly ? "Overdue" : "",
    status: initial.status ?? "",
    from: "",
    to: "",
    search: "",
  });

  const reload = useCallback(() => {
    const params = new URLSearchParams();
    if (filters.urgency) params.set("urgency", filters.urgency);
    if (filters.status) params.set("status", filters.status);
    if (filters.from) params.set("from", filters.from);
    if (filters.to) params.set("to", filters.to);
    if (filters.search.trim()) params.set("search", filters.search.trim());
    api
      .get<InstallmentWorklistRowDto[]>(`/installments?${params.toString()}`)
      .then((data) => {
        setRows(data);
        setError(null);
      })
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری فهرست"));
    api
      .get<InstallmentCountsDto>("/installments/counts")
      .then(setCounts)
      .catch(() => {});
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [filters]);

  useEffect(() => {
    reload();
  }, [reload]);

  useLiveReload(reload);

  const hasActiveFilters = filters.urgency || filters.status || filters.from || filters.to || filters.search.trim();
  const bucketCount = (u: InstallmentWorklistRowDto["urgency"]) =>
    counts?.buckets.find((b) => b.urgency === u)?.count ?? 0;
  const overdueBucket = counts?.buckets.find((b) => b.urgency === "Overdue");

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">فهرست اقساط</h2>
      <div className="mb-4.5 text-[12.5px] text-(--ice-3)">مرتب‌شده بر اساس تاریخ سررسید</div>

      {overdueBucket && overdueBucket.count > 0 && filters.urgency !== "Overdue" && (
        <div
          onClick={() => setFilters({ ...filters, urgency: "Overdue" })}
          className="mb-4.5 flex cursor-pointer items-center gap-3 rounded-[12px] border border-(--ember)/25 bg-(--ember)/8 px-3.5 py-2.5 transition-colors hover:bg-(--ember)/12"
        >
          <span className="text-[16px]">⚠</span>
          <div className="flex-1 text-[12.5px] text-(--ice-2)">
            <b className="font-bold text-(--ember)">{fa(overdueBucket.count)} قسط معوق</b> با ماندهٔ باز{" "}
            <b className="font-bold text-(--ember)">{money(overdueBucket.balance)}</b> تومان — مهلت تسویه به بیمه‌گر گذشته است.
          </div>
          <span className="text-[11.5px] text-(--ice-3)">مشاهدهٔ معوق‌ها ←</span>
        </div>
      )}

      <div className="mb-3 flex flex-wrap items-center gap-2">
        <button
          type="button"
          onClick={() => setFilters({ ...filters, urgency: "" })}
          className={`rounded-[10px] border px-3 py-1.5 text-[12.5px] font-semibold transition-colors ${
            !filters.urgency
              ? "border-(--mint) bg-(--mint) text-(--on-mint)"
              : "border-(--edge-2) bg-(--btn-bg) text-(--ice-2) hover:bg-(--btn-hov)"
          }`}
        >
          همه {counts !== null && <span className="ms-1 opacity-80">{fa(counts.total)}</span>}
        </button>
        {URGENCY_CHIP_ORDER.map((u) => {
          const active = filters.urgency === u;
          const count = bucketCount(u);
          const hot = u === "Overdue" || u === "Critical";
          return (
            <button
              key={u}
              type="button"
              onClick={() => setFilters({ ...filters, urgency: active ? "" : u })}
              className={`rounded-[10px] border px-3 py-1.5 text-[12.5px] font-semibold transition-colors ${
                active
                  ? "border-(--mint) bg-(--mint) text-(--on-mint)"
                  : hot && count > 0
                    ? "border-(--ember)/35 bg-(--ember)/8 text-(--ember) hover:bg-(--ember)/14"
                    : "border-(--edge-2) bg-(--btn-bg) text-(--ice-2) hover:bg-(--btn-hov)"
              }`}
            >
              {URGENCY_LABEL[u]}
              {count > 0 && <span className="ms-1 opacity-80">{fa(count)}</span>}
            </button>
          );
        })}
      </div>

      <div className="mb-4.5 flex flex-wrap items-end gap-2.5">
        <select
          value={filters.status}
          onChange={(e) => setFilters({ ...filters, status: e.target.value })}
          className="rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-1.5 text-[12.5px] text-(--ice) outline-none focus:border-(--mint)"
        >
          {STATUS_OPTIONS.map((o) => (
            <option key={o.value} value={o.value}>
              {o.label}
            </option>
          ))}
        </select>
        <div>
          <div className="mb-1 text-[10.5px] text-(--ice-3)">از تاریخ سررسید</div>
          <JalaliDateField
            value={filters.from}
            onChange={(iso) => setFilters({ ...filters, from: iso })}
            className="w-36"
          />
        </div>
        <div>
          <div className="mb-1 text-[10.5px] text-(--ice-3)">تا تاریخ سررسید</div>
          <JalaliDateField
            value={filters.to}
            onChange={(iso) => setFilters({ ...filters, to: iso })}
            className="w-36"
          />
        </div>
        <div className="grow basis-48">
          <div className="mb-1 text-[10.5px] text-(--ice-3)">جستجو</div>
          <input
            value={filters.search}
            onChange={(e) => setFilters({ ...filters, search: e.target.value })}
            placeholder="شمارهٔ بیمه‌نامه، نام، موبایل، کد ملی، پلاک"
            className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-1.5 text-[12.5px] text-(--ice) outline-none focus:border-(--mint)"
          />
        </div>
        {hasActiveFilters && (
          <button
            type="button"
            onClick={() =>
              setFilters({ urgency: "", status: "", from: "", to: "", search: "" })
            }
            className="pb-0.5 text-[11.5px] text-(--ice-3) hover:text-(--ice)"
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
          <div className="mb-2 text-[11.5px] text-(--ice-3)">{fa(rows.length)} قسط</div>
          {rows.length === 0 ? (
            <EmptyState
              icon="🧾"
              title="موردی یافت نشد"
              description={
                hasActiveFilters
                  ? "هیچ قسطی با این فیلترها مطابقت ندارد. فیلترها را پاک کنید یا بازهٔ تاریخ را تغییر دهید."
                  : "هنوز قسط بازِ تسویه‌نشده‌ای وجود ندارد — همه‌چیز تسویه شده است."
              }
              action={
                hasActiveFilters
                  ? { label: "پاک کردن فیلترها", onClick: () => setFilters({ urgency: "", status: "", from: "", to: "", search: "" }) }
                  : undefined
              }
            />
          ) : (
            <Table>
              <thead>
                <tr>
                  {["بیمه‌نامه", "بیمه‌گذار", "موبایل", "قسط", "سررسید", "وضعیت", "مانده", ""].map((h) => (
                    <Th key={h}>{h}</Th>
                  ))}
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
                    <Td className="py-2.75 font-semibold">{r.policyNumber}</Td>
                    <Td className="py-2.75 text-(--ice-3)">{r.customerFullName}</Td>
                    <Td className="py-2.75 tabular-nums text-(--ice-3)" ltr>
                      {r.customerMobile ? fa(r.customerMobile) : "—"}
                    </Td>
                    <Td className="py-2.75">{fa(r.seqNo)}</Td>
                    <Td className="py-2.75">{toJalaliDisplay(r.dueDate)}</Td>
                    <Td className="py-2.75">
                      <StatusBadge tone={URGENCY_TONE[r.urgency]}>{URGENCY_LABEL[r.urgency]}</StatusBadge>
                    </Td>
                    <Td className="py-2.75 font-bold">{money(r.balance)}</Td>
                    <Td className="py-2.75">
                      <div className="flex gap-1.5">
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
                        {r.status !== "Settled" && (
                          <button
                            type="button"
                            onClick={(e) => {
                              e.stopPropagation();
                              setEditingRow({
                                installmentId: r.installmentId,
                                seqNo: r.seqNo,
                                dueDate: r.dueDate,
                                amount: r.amount,
                                status: r.status,
                              });
                            }}
                            className="rounded-[8px] border border-(--edge-2) px-2.5 py-1 text-[11.5px] text-(--ice-2) transition-colors hover:bg-(--hov)"
                          >
                            ویرایش
                          </button>
                        )}
                      </div>
                    </Td>
                  </Tr>
                ))}
              </tbody>
            </Table>
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

      {editingRow && (
        <EditInstallmentDialog
          target={editingRow}
          onClose={() => setEditingRow(null)}
          onSaved={reload}
        />
      )}
    </div>
  );
}
