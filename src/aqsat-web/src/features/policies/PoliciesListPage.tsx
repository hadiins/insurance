import { useEffect, useState } from "react";
import { useTabsStore } from "../../app/store/tabsStore";
import { useTabKey } from "../shell/TabContext";
import { useDraftState } from "../shell/useDraftState";
import { useLiveReload } from "../shell/useLiveReload";
import { api, ApiError } from "../../lib/api";
import { fa, money } from "../../lib/persian";
import { StatusBadge } from "../../components/StatusBadge";
import { EmptyState } from "../../components/EmptyState";
import { Table, Td, Th, Tr } from "../../components/Table";

interface PolicyListFilterPayload {
  status?: string;
  isInstallment?: boolean;
}

interface PolicyListItemDto {
  id: string;
  policyNumber: string;
  customerFullName: string;
  insuranceLineNameFa: string;
  status: "Active" | "Settled" | "Cancelled" | "PendingConfirmation";
  isInstallment: boolean;
  totalReceivable: number;
  balance: number;
  issueDate: string;
}

const STATUS_LABEL: Record<PolicyListItemDto["status"], string> = {
  Active: "فعال",
  Settled: "تسویه‌شده",
  Cancelled: "باطل‌شده",
  PendingConfirmation: "در انتظار تأیید",
};

const STATUS_TONE: Record<PolicyListItemDto["status"], "mint" | "moss" | "amber" | "ember"> = {
  Active: "mint",
  Settled: "moss",
  Cancelled: "ember",
  PendingConfirmation: "amber",
};

const STATUS_OPTIONS: { value: string; label: string }[] = [
  { value: "", label: "همهٔ وضعیت‌ها" },
  { value: "Active", label: "فعال" },
  { value: "Settled", label: "تسویه‌شده" },
  { value: "Cancelled", label: "باطل‌شده" },
  { value: "PendingConfirmation", label: "در انتظار تأیید مشتری" },
];

const INSTALLMENT_OPTIONS: { value: string; label: string }[] = [
  { value: "", label: "همه" },
  { value: "true", label: "فقط اقساطی" },
  { value: "false", label: "فقط غیراقساطی" },
];

export function PoliciesListPage() {
  const tabKey = useTabKey();
  const tab = useTabsStore((s) => s.tabs.find((t) => t.key === tabKey));
  const openTab = useTabsStore((s) => s.openTab);
  const initial = (tab?.payload as PolicyListFilterPayload | undefined) ?? {};

  const [filters, setFilters] = useDraftState<{ search: string; status: string; installment: string }>(
    "list-filters",
    {
      search: "",
      status: initial.status ?? "",
      installment: initial.isInstallment ? "true" : "",
    },
  );
  const search = filters.search;
  const status = filters.status;
  const installmentFilter = filters.installment;
  const [items, setItems] = useState<PolicyListItemDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  function reload() {
    const params = new URLSearchParams();
    if (search.trim()) params.set("search", search.trim());
    if (status) params.set("status", status);
    if (installmentFilter) params.set("isInstallment", installmentFilter);
    api
      .get<PolicyListItemDto[]>(`/policies?${params.toString()}`)
      .then((data) => {
        setItems(data);
        setError(null);
      })
      .catch((err) => setError(err instanceof ApiError ? err.message : "خطا در بارگذاری فهرست"));
  }

  useEffect(() => {
    const handle = setTimeout(reload, 250);
    return () => clearTimeout(handle);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [search, status, installmentFilter]);

  useLiveReload(reload);

  function openPolicy(p: PolicyListItemDto) {
    openTab({
      navType: "policy-file",
      page: "policy-file",
      kind: "multi-record",
      recordId: p.id,
      title: p.policyNumber,
      payload: { policyId: p.id },
    });
  }

  async function confirm(id: string, e: React.MouseEvent) {
    e.stopPropagation();
    setError(null);
    try {
      await api.put(`/policies/${id}/confirm`, {});
      reload();
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "تأیید ناموفق بود.");
    }
  }

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">فهرست بیمه‌نامه‌ها</h2>
      <div className="mb-4.5 text-[12.5px] text-(--ice-3)">
        جست‌وجو بر اساس شمارهٔ کامل، سریال، سال، کد رشته، نام، موبایل، کد ملی یا پلاک خودرو
      </div>

      <div className="mb-4.5 flex max-w-sm items-center gap-1.5">
        <input
          value={search}
          onChange={(e) => setFilters({ ...filters, search: e.target.value })}
          placeholder="شمارهٔ بیمه‌نامه، نام، موبایل، کد ملی، پلاک…"
          dir="ltr"
          className="w-full rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-right text-[13.5px] text-(--ice) outline-none focus:border-(--mint)"
        />
        {search && (
          <button
            type="button"
            onClick={() => setFilters({ ...filters, search: "" })}
            className="shrink-0 rounded-[10px] border border-(--edge-2) bg-(--btn-bg) px-2.5 py-2 text-[11.5px] text-(--ice-3) transition-colors hover:bg-(--btn-hov) hover:text-(--ice)"
          >
            پاک کردن
          </button>
        )}
      </div>

      <div className="mb-4.5 flex flex-wrap items-center gap-2">
        <select
          value={status}
          onChange={(e) => setFilters({ ...filters, status: e.target.value })}
          className="rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-1.5 text-[12.5px] text-(--ice) outline-none focus:border-(--mint)"
        >
          {STATUS_OPTIONS.map((o) => (
            <option key={o.value} value={o.value}>
              {o.label}
            </option>
          ))}
        </select>
        <select
          value={installmentFilter}
          onChange={(e) => setFilters({ ...filters, installment: e.target.value })}
          className="rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-1.5 text-[12.5px] text-(--ice) outline-none focus:border-(--mint)"
        >
          {INSTALLMENT_OPTIONS.map((o) => (
            <option key={o.value} value={o.value}>
              {o.label}
            </option>
          ))}
        </select>
        {(status || installmentFilter) && (
          <button
            type="button"
            onClick={() => setFilters({ ...filters, status: "", installment: "" })}
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

      {!error && items === null && <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>}

      {!error && items !== null && (
        <>
          <div className="mb-2 text-[11.5px] text-(--ice-3)">{fa(items.length)} بیمه‌نامه</div>
          {items.length === 0 ? (
            <EmptyState
              icon="📄"
              title="بیمه‌نامه‌ای یافت نشد"
              description={
                search || status || installmentFilter
                  ? "هیچ بیمه‌نامه‌ای با این فیلترها مطابقت ندارد. فیلترها را پاک کنید یا عبارت جستجو را تغییر دهید."
                  : "هنوز بیمه‌نامه‌ای ثبت نشده است."
              }
              action={
                status || installmentFilter
                  ? { label: "پاک کردن فیلترها", onClick: () => setFilters({ ...filters, status: "", installment: "" }) }
                  : undefined
              }
            />
          ) : (
            <Table>
              <thead>
                <tr>
                  {["شمارهٔ بیمه‌نامه", "بیمه‌گذار", "رشته", "وضعیت", "مبلغ کل", "مانده", ""].map((h) => (
                    <Th key={h}>{h}</Th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {items.map((p) => (
                  <Tr key={p.id} onClick={() => openPolicy(p)}>
                    <Td className="py-2.75 font-semibold">{p.policyNumber}</Td>
                    <Td className="py-2.75 text-(--ice-3)">{p.customerFullName}</Td>
                    <Td className="py-2.75 text-(--ice-3)">{p.insuranceLineNameFa}</Td>
                    <Td className="py-2.75">
                      <StatusBadge tone={STATUS_TONE[p.status]}>{STATUS_LABEL[p.status]}</StatusBadge>
                    </Td>
                    <Td className="py-2.75">{money(p.totalReceivable)}</Td>
                    <Td className="py-2.75 font-bold">{money(p.balance)}</Td>
                    <Td className="py-2.75">
                      {p.status === "PendingConfirmation" && (
                        <button
                          type="button"
                          onClick={(e) => confirm(p.id, e)}
                          className="rounded-[8px] border border-(--mint) bg-(--mint) px-2.5 py-1 text-[11.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105"
                        >
                          تأیید شد
                        </button>
                      )}
                    </Td>
                  </Tr>
                ))}
              </tbody>
            </Table>
          )}
        </>
      )}
    </div>
  );
}
