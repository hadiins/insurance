import { useCallback, useEffect, useState } from "react";
import { useTabsStore } from "../../app/store/tabsStore";
import { useTabKey } from "../shell/TabContext";
import { useLiveReload } from "../shell/useLiveReload";
import { api, ApiError } from "../../lib/api";
import { fa, money } from "../../lib/persian";
import { toJalaliDisplay } from "../../lib/jalali";
import { StatusBadge } from "../../components/StatusBadge";
import { Table, Td, Th, Tr } from "../../components/Table";
import { EmptyState } from "../../components/EmptyState";

interface ChequesFilterPayload {
  status?: string;
  upcomingDays?: string;
}

interface UnifiedChequeRowDto {
  source: "Collateral" | "Payment";
  id: string;
  policyId: string;
  policyNumber: string;
  customerName: string;
  chequeNumber: string | null;
  sayadId: string | null;
  bankName: string;
  amount: number;
  dueDate: string | null;
  status: "Held" | "AtBank" | "Cleared" | "Bounced";
  presenterName: string | null;
  cashBoxName: string | null;
}

const SOURCE_LABEL: Record<UnifiedChequeRowDto["source"], string> = {
  Collateral: "وثیقهٔ صیادی",
  Payment: "چک دریافتی",
};

const STATUS_LABEL: Record<UnifiedChequeRowDto["status"], string> = {
  Held: "نزد نماینده",
  AtBank: "نزد بانک",
  Cleared: "پاس‌شده",
  Bounced: "برگشتی",
};

const STATUS_TONE: Record<UnifiedChequeRowDto["status"], "mint" | "amber" | "ember"> = {
  Held: "mint",
  AtBank: "amber",
  Cleared: "mint",
  Bounced: "ember",
};

/** GET /api/cheques merges guarantee cheques (Collateral) with cheques received toward payments
 * (PaymentCheque). Status changes dispatch to the matching per-source endpoint — PaymentCheque's
 * bounce path reverses the payment via PaymentReversalService, so it must stay the only route. */
export function ChequesListPage() {
  const tabKey = useTabKey();
  const tab = useTabsStore((s) => s.tabs.find((t) => t.key === tabKey));
  const openTab = useTabsStore((s) => s.openTab);
  const initial = (tab?.payload as ChequesFilterPayload | undefined) ?? {};

  const [rows, setRows] = useState<UnifiedChequeRowDto[] | null>(null);
  const [statusFilter, setStatusFilter] = useState(initial.status ?? "");
  const [upcomingOnly, setUpcomingOnly] = useState(Boolean(initial.upcomingDays));
  const [sourceFilter, setSourceFilter] = useState<"" | "Collateral" | "Payment">("");
  const [error, setError] = useState<string | null>(null);

  const reload = useCallback(() => {
    const params = new URLSearchParams();
    if (statusFilter) params.set("status", statusFilter);
    if (upcomingOnly) params.set("upcomingDays", "30");
    api
      .get<UnifiedChequeRowDto[]>(`/cheques?${params.toString()}`)
      .then((data) => {
        setRows(data);
        setError(null);
      })
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری فهرست چک‌ها"));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [statusFilter, upcomingOnly]);

  useEffect(() => {
    reload();
  }, [reload]);

  useLiveReload(reload);

  async function setStatus(row: UnifiedChequeRowDto, status: UnifiedChequeRowDto["status"]) {
    setError(null);
    const path = row.source === "Collateral" ? "/collateral" : "/payment-cheques";
    try {
      await api.put(`${path}/${row.id}/status`, { status });
      reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "به‌روزرسانی وضعیت ناموفق بود.");
    }
  }

  const visible = rows?.filter((r) => !sourceFilter || r.source === sourceFilter) ?? null;
  const hasActiveFilters = Boolean(statusFilter || upcomingOnly || sourceFilter);

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">فهرست چک‌ها</h2>
      <div className="mb-4.5 text-[12.5px] text-(--ice-3)">
        همهٔ چک‌ها در یک نگاه — چه وثیقهٔ صیادی باشند، چه در قبال اقساط دریافت شده باشند
      </div>

      <div className="mb-3 flex flex-wrap gap-2">
        <select
          value={sourceFilter}
          onChange={(e) => setSourceFilter(e.target.value as "" | "Collateral" | "Payment")}
          className="rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-1.5 text-[12.5px] text-(--ice) outline-none focus:border-(--mint)"
        >
          <option value="">همهٔ انواع</option>
          <option value="Collateral">وثیقهٔ صیادی</option>
          <option value="Payment">چک دریافتی از اقساط</option>
        </select>
        <select
          value={statusFilter}
          onChange={(e) => setStatusFilter(e.target.value)}
          className="rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-1.5 text-[12.5px] text-(--ice) outline-none focus:border-(--mint)"
        >
          <option value="">همهٔ وضعیت‌ها</option>
          {(["Held", "AtBank", "Cleared", "Bounced"] as const).map((s) => (
            <option key={s} value={s}>
              {STATUS_LABEL[s]}
            </option>
          ))}
        </select>
        <button
          type="button"
          onClick={() => setUpcomingOnly((v) => !v)}
          className={`rounded-full px-3 py-1 text-[11.5px] font-semibold transition-colors ${
            upcomingOnly ? "bg-(--mint) text-(--on-mint)" : "border border-(--edge-2) text-(--ice-3) hover:bg-(--hov)"
          }`}
        >
          فقط پیشِ رو (۳۰ روز)
        </button>
        {hasActiveFilters && (
          <button
            type="button"
            onClick={() => {
              setSourceFilter("");
              setStatusFilter("");
              setUpcomingOnly(false);
            }}
            className="rounded-full px-3 py-1 text-[11.5px] text-(--ice-3) underline underline-offset-2 transition-colors hover:text-(--ice)"
          >
            پاک‌کردن فیلترها
          </button>
        )}
      </div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      {visible !== null && <div className="mb-2 text-[11.5px] text-(--ice-3)">{fa(visible.length)} چک</div>}

      {visible === null ? (
        <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>
      ) : visible.length === 0 ? (
        <EmptyState
          icon="🧾"
          title="چکی یافت نشد"
          description={
            hasActiveFilters
              ? "هیچ چکی با این فیلترها مطابقت ندارد. فیلترها را پاک کنید یا بازه را تغییر دهید."
              : "هنوز چیزی — نه وثیقهٔ صیادی و نه چک دریافتی — ثبت نشده است."
          }
          action={
            hasActiveFilters
              ? {
                  label: "پاک کردن فیلترها",
                  onClick: () => {
                    setSourceFilter("");
                    setStatusFilter("");
                    setUpcomingOnly(false);
                  },
                }
              : undefined
          }
        />
      ) : (
        <Table>
          <thead>
            <tr>
              {["نوع", "بیمه‌نامه", "بیمه‌گذار", "شمارهٔ چک", "بانک", "مبلغ", "سررسید", "وضعیت", ""].map((h) => (
                <Th key={h}>{h}</Th>
              ))}
            </tr>
          </thead>
          <tbody>
            {visible.map((c) => (
              <Tr
                key={`${c.source}:${c.id}`}
                onClick={() =>
                  openTab({
                    navType: "policy-file",
                    page: "policy-file",
                    kind: "multi-record",
                    recordId: c.policyId,
                    title: c.policyNumber,
                    payload: { policyId: c.policyId },
                  })
                }
              >
                <Td className="py-2.5 !text-[12.5px] text-(--ice-3)">{SOURCE_LABEL[c.source]}</Td>
                <Td className="py-2.5 font-semibold">{fa(c.policyNumber)}</Td>
                <Td className="py-2.5 text-(--ice-3)">{c.customerName}</Td>
                <Td ltr className="py-2.5 tabular-nums text-(--ice-3)">
                  {fa(c.chequeNumber ?? c.sayadId ?? "—")}
                </Td>
                <Td className="py-2.5 text-(--ice-3)">{c.bankName || "—"}</Td>
                <Td className="py-2.5 font-bold">{money(c.amount)}</Td>
                <Td className="py-2.5">{c.dueDate ? toJalaliDisplay(c.dueDate) : "—"}</Td>
                <Td className="py-2.5">
                  <StatusBadge tone={STATUS_TONE[c.status]}>{STATUS_LABEL[c.status]}</StatusBadge>
                </Td>
                <Td className="py-2.5" onClick={(e) => e.stopPropagation()}>
                  <div className="flex gap-1.5">
                    {c.status === "Held" && (
                      <button
                        type="button"
                        onClick={() => setStatus(c, "AtBank")}
                        className="rounded-[8px] border border-(--edge-2) px-2 py-1 text-[10.5px] text-(--ice-3) transition-colors hover:bg-(--hov)"
                      >
                        نزد بانک
                      </button>
                    )}
                    {(c.status === "Held" || c.status === "AtBank") && (
                      <>
                        <button
                          type="button"
                          onClick={() => setStatus(c, "Cleared")}
                          className="rounded-[8px] border border-(--mint) px-2 py-1 text-[10.5px] text-(--mint) transition-colors hover:bg-(--mint)/10"
                        >
                          پاس‌شد
                        </button>
                        <button
                          type="button"
                          onClick={() => setStatus(c, "Bounced")}
                          className="rounded-[8px] border border-(--ember) px-2 py-1 text-[10.5px] text-(--ember) transition-colors hover:bg-(--ember)/10"
                        >
                          برگشت خورد
                        </button>
                      </>
                    )}
                  </div>
                </Td>
              </Tr>
            ))}
          </tbody>
        </Table>
      )}
    </div>
  );
}
