import { useState } from "react";
import { useTabsStore } from "../../app/store/tabsStore";
import { api, ApiError } from "../../lib/api";
import { fa, toLatinDigits } from "../../lib/persian";
import { EmptyState } from "../../components/EmptyState";
import { CustomerRiskPanel } from "./CustomerRiskPanel";

interface CustomerListItemDto {
  id: string;
  fullName: string;
  mobile: string | null;
  policyCount: number;
}

/** «ارزیابی اعتبار» (docs Phase 2A §7) — find a customer by national ID / name / mobile, run the
 * assessment, then jump into the full customer file for the issuing decision. */
export function CreditAssessmentPage() {
  const openTab = useTabsStore((s) => s.openTab);
  const [query, setQuery] = useState("");
  const [customers, setCustomers] = useState<CustomerListItemDto[] | null>(null);
  const [selected, setSelected] = useState<CustomerListItemDto | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function search() {
    const q = toLatinDigits(query.trim());
    if (!q) return;
    setError(null);
    setSelected(null);
    try {
      const results = await api.get<CustomerListItemDto[]>(`/customers?search=${encodeURIComponent(q)}`);
      setCustomers(results);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "جست‌وجو ناموفق بود.");
    }
  }

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">ارزیابی اعتبار مشتری</h2>
      <div className="mb-4.5 text-[12.5px] text-(--ice-3)">
        استعلام امتیاز اعتباری مشتری قبل از صدور بیمه‌نامهٔ اقساطی — سابقهٔ پرداخت، معوقات، چک برگشتی و سقف اعتبار
      </div>

      {error && (
        <div className="mb-4.5 rounded-[10px] border border-(--ember)/30 bg-(--ember)/10 px-3 py-2 text-[12.5px] text-(--ember)">
          {error}
        </div>
      )}

      <div className="mb-4.5 flex gap-2">
        <input
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          onKeyDown={(e) => e.key === "Enter" && search()}
          placeholder="کد ملی، نام یا موبایل مشتری…"
          dir="ltr"
          className="flex-1 rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13.5px] text-(--ice) outline-none focus:border-(--mint)"
        />
        <button
          type="button"
          onClick={search}
          className="rounded-[10px] border border-(--mint) bg-(--mint) px-4 py-2 text-[12.5px] font-semibold text-(--on-mint) transition-colors hover:brightness-105"
        >
          جست‌وجو
        </button>
      </div>

      {selected ? (
        <div>
          <div className="mb-3 flex items-center justify-between">
            <b className="text-[14px] text-(--ice)">{selected.fullName}</b>
            <div className="flex items-center gap-3">
              <button
                type="button"
                onClick={() =>
                  openTab({
                    navType: "customer-file",
                    page: "customer-file",
                    kind: "multi-record",
                    recordId: selected.id,
                    title: selected.fullName,
                    payload: { customerId: selected.id, customerName: selected.fullName },
                  })
                }
                className="text-[11.5px] font-semibold text-(--mint) underline underline-offset-2 hover:brightness-110"
              >
                پروندهٔ کامل مشتری
              </button>
              <button
                type="button"
                onClick={() => setSelected(null)}
                className="text-[11.5px] text-(--ice-3) underline underline-offset-2 hover:text-(--ice)"
              >
                بازگشت به نتایج
              </button>
            </div>
          </div>
          <CustomerRiskPanel customerId={selected.id} />
        </div>
      ) : customers !== null ? (
        customers.length === 0 ? (
          <EmptyState
            icon="🔍"
            title="مشتری‌ای یافت نشد."
            description="عبارت جستجو را تغییر دهید و دوباره امتحان کنید."
          />
        ) : (
          <div className="space-y-1.5">
            {customers.map((c) => (
              <button
                key={c.id}
                type="button"
                onClick={() => setSelected(c)}
                className="block w-full rounded-[10px] border border-(--edge-2) px-3 py-2 text-right text-[12.5px] text-(--ice-2) transition-colors hover:bg-(--hov)"
              >
                {c.fullName} <span className="text-(--ice-3)">— {c.mobile ?? "بدون شماره"} — {fa(c.policyCount)} بیمه‌نامه</span>
              </button>
            ))}
          </div>
        )
      ) : (
        <EmptyState
          icon="📊"
          title="مشتری را جست‌وجو کنید."
          description="پس از انتخاب مشتری، امتیاز اعتباری، عوامل مؤثر و سقف اعتبار او نمایش داده می‌شود."
        />
      )}
    </div>
  );
}
