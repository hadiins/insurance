import { useEffect, useState } from "react";
import { useTabsStore } from "../../app/store/tabsStore";
import { useTabKey } from "../shell/TabContext";
import { api, ApiError } from "../../lib/api";
import { fa, money } from "../../lib/persian";

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

const STATUS_PILL_CLASS: Record<PolicyListItemDto["status"], string> = {
  Active: "bg-(--mint)/12 text-(--mint)",
  Settled: "bg-(--mint)/12 text-(--mint)",
  Cancelled: "bg-(--ember)/13 text-(--ember)",
  PendingConfirmation: "bg-(--amber)/13 text-(--amber)",
};

const TITLES: Record<string, string> = {
  none: "فهرست بیمه‌نامه‌ها",
  installment: "بیمه‌نامه‌های اقساطی",
  cancelled: "باطل‌شده‌ها",
  pending: "در انتظار تأیید مشتری",
};

export function PoliciesListPage() {
  const tabKey = useTabKey();
  const tab = useTabsStore((s) => s.tabs.find((t) => t.key === tabKey));
  const openTab = useTabsStore((s) => s.openTab);
  const initial = (tab?.payload as PolicyListFilterPayload | undefined) ?? {};

  const [search, setSearch] = useState("");
  const [items, setItems] = useState<PolicyListItemDto[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  const heading = initial.isInstallment
    ? TITLES.installment
    : initial.status === "Cancelled"
      ? TITLES.cancelled
      : initial.status === "PendingConfirmation"
        ? TITLES.pending
        : TITLES.none;

  function reload() {
    const params = new URLSearchParams();
    if (search.trim()) params.set("search", search.trim());
    if (initial.status) params.set("status", initial.status);
    if (initial.isInstallment) params.set("isInstallment", "true");
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
  }, [search, initial.status, initial.isInstallment]);

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
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">{heading}</h2>
      <div className="mb-4.5 text-xs text-(--ice-3)">جست‌وجو بر اساس شمارهٔ بیمه‌نامه یا نام بیمه‌گذار</div>

      <input
        value={search}
        onChange={(e) => setSearch(e.target.value)}
        placeholder="شمارهٔ بیمه‌نامه یا نام…"
        className="mb-4.5 w-full max-w-sm rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13px] text-(--ice) outline-none focus:border-(--mint)"
      />

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      {!error && items === null && <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>}

      {!error && items !== null && (
        <>
          <div className="mb-2 text-[11px] text-(--ice-3)">{fa(items.length)} بیمه‌نامه</div>
          {items.length === 0 ? (
            <div className="rounded-2xl border border-(--edge) bg-(--pane) p-6 text-center text-[13px] text-(--ice-3)">بیمه‌نامه‌ای یافت نشد.</div>
          ) : (
            <div className="overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
              <table className="w-full border-collapse">
                <thead>
                  <tr>
                    {["شمارهٔ بیمه‌نامه", "بیمه‌گذار", "رشته", "وضعیت", "مبلغ کل", "مانده", ""].map((h) => (
                      <th key={h} className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                        {h}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {items.map((p) => (
                    <tr
                      key={p.id}
                      onClick={() => openPolicy(p)}
                      className="cursor-pointer border-t border-(--edge) transition-colors first:border-t-0 hover:bg-(--hov)"
                    >
                      <td className="px-3 py-2.75 text-[13px] font-semibold">{p.policyNumber}</td>
                      <td className="px-3 py-2.75 text-[13px] text-(--ice-3)">{p.customerFullName}</td>
                      <td className="px-3 py-2.75 text-[13px] text-(--ice-3)">{p.insuranceLineNameFa}</td>
                      <td className="px-3 py-2.75 text-[13px]">
                        <span className={`rounded-full px-2.5 py-0.5 text-[11px] font-semibold ${STATUS_PILL_CLASS[p.status]}`}>
                          {STATUS_LABEL[p.status]}
                        </span>
                      </td>
                      <td className="px-3 py-2.75 text-[13px]">{money(p.totalReceivable)}</td>
                      <td className="px-3 py-2.75 text-[13px] font-bold">{money(p.balance)}</td>
                      <td className="px-3 py-2.75 text-[13px]">
                        {p.status === "PendingConfirmation" && (
                          <button
                            type="button"
                            onClick={(e) => confirm(p.id, e)}
                            className="rounded-[8px] border border-(--mint) bg-(--mint) px-2.5 py-1 text-[11px] font-semibold text-(--on-mint) transition-colors hover:brightness-105"
                          >
                            تأیید شد
                          </button>
                        )}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
        </>
      )}
    </div>
  );
}
