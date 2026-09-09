import { useState } from "react";
import { useTabsStore } from "../../app/store/tabsStore";
import { useTabKey } from "../shell/TabContext";
import { api, ApiError } from "../../lib/api";
import { fa, money } from "../../lib/persian";
import { toJalaliDisplay } from "../../lib/jalali";
import { EmptyState } from "../../components/EmptyState";
import { Table, Td, Th, Tr } from "../../components/Table";

type Mode = "payments" | "statement";

interface CustomerListItemDto {
  id: string;
  fullName: string;
  mobile: string | null;
  policyCount: number;
}

interface CustomerPolicySummaryDto {
  policyId: string;
  policyNumber: string;
  insuranceLineNameFa: string;
  status: string;
  totalReceivable: number;
  balance: number;
}

interface CustomerPaymentDto {
  id: string;
  amount: number;
  paidOn: string;
  method: string;
  referenceNo: string | null;
  allocatedTo: string[];
}

interface CustomerFileDto {
  customerId: string;
  fullName: string;
  mobile: string | null;
  aggregateBalance: number;
  policies: CustomerPolicySummaryDto[];
  payments: CustomerPaymentDto[];
}

const HEADING: Record<Mode, { title: string; sub: string }> = {
  payments: { title: "سابقهٔ پرداخت", sub: "جست‌وجوی مشتری و مشاهدهٔ همهٔ پرداخت‌های ثبت‌شده" },
  statement: { title: "صورت‌حساب مشتری", sub: "جمع بدهی و وضعیت هر بیمه‌نامهٔ مشتری" },
};

export function CustomerLookupPage() {
  const tabKey = useTabKey();
  const tab = useTabsStore((s) => s.tabs.find((t) => t.key === tabKey));
  const mode = ((tab?.payload as { mode?: Mode } | undefined)?.mode ?? "statement") as Mode;

  const [query, setQuery] = useState("");
  const [customers, setCustomers] = useState<CustomerListItemDto[] | null>(null);
  const [selected, setSelected] = useState<CustomerFileDto | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function search() {
    if (!query.trim()) return;
    setError(null);
    setSelected(null);
    try {
      const results = await api.get<CustomerListItemDto[]>(`/customers?search=${encodeURIComponent(query.trim())}`);
      setCustomers(results);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "جست‌وجو ناموفق بود.");
    }
  }

  async function selectCustomer(customer: CustomerListItemDto) {
    setError(null);
    try {
      const file = await api.get<CustomerFileDto>(`/customers/${customer.id}/file`);
      setSelected(file);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "خطا در بارگذاری پروندهٔ مشتری");
    }
  }

  const { title, sub } = HEADING[mode];

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">{title}</h2>
      <div className="mb-4.5 text-[12.5px] text-(--ice-3)">{sub}</div>

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
          placeholder="نام، کد ملی، موبایل یا پلاک خودرو…"
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

      {customers !== null && !selected && (
        <div>
          <div className="mb-2 text-[11.5px] text-(--ice-3)">{fa(customers.length)} نتیجه</div>
          {customers.length === 0 ? (
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
                  onClick={() => selectCustomer(c)}
                  className="block w-full rounded-[10px] border border-(--edge-2) px-3 py-2 text-right text-[12.5px] text-(--ice-2) transition-colors hover:bg-(--hov)"
                >
                  {c.fullName} <span className="text-(--ice-3)">— {c.mobile ?? "بدون شماره"} — {fa(c.policyCount)} بیمه‌نامه</span>
                </button>
              ))}
            </div>
          )}
        </div>
      )}

      {selected && (
        <div>
          <div className="mb-3 flex items-center justify-between">
            <b className="text-[14px] text-(--ice)">{selected.fullName}</b>
            <button
              type="button"
              onClick={() => setSelected(null)}
              className="text-[11.5px] text-(--ice-3) underline underline-offset-2 hover:text-(--ice)"
            >
              بازگشت به نتایج جست‌وجو
            </button>
          </div>

          {mode === "statement" ? (
            <>
              <div className="mb-4.5 rounded-2xl border border-(--edge) bg-(--pane) p-4">
                <div className="mb-1 text-[10.5px] tracking-[0.16em] text-(--ice-3)">جمع بدهی</div>
                <div className="text-[20px] font-extrabold text-(--ice)">{money(selected.aggregateBalance)}</div>
              </div>
              {selected.policies.length === 0 ? (
                <EmptyState
                  icon="📄"
                  title="بیمه‌نامه‌ای ندارد."
                  description="این مشتری هنوز بیمه‌نامه‌ای در سیستم ندارد."
                />
              ) : (
                <Table>
                  <thead>
                    <tr>
                      {["بیمه‌نامه", "رشته", "وضعیت", "مبلغ کل", "مانده"].map((h) => (
                        <Th key={h}>{h}</Th>
                      ))}
                    </tr>
                  </thead>
                  <tbody>
                    {selected.policies.map((p) => (
                      <Tr key={p.policyId}>
                        <Td className="py-2.5 font-semibold">{p.policyNumber}</Td>
                        <Td className="py-2.5 text-(--ice-3)">{p.insuranceLineNameFa}</Td>
                        <Td className="py-2.5 text-(--ice-3)">{p.status}</Td>
                        <Td className="py-2.5">{money(p.totalReceivable)}</Td>
                        <Td className="py-2.5 font-bold">{money(p.balance)}</Td>
                      </Tr>
                    ))}
                  </tbody>
                </Table>
              )}
            </>
          ) : selected.payments.length === 0 ? (
            <EmptyState
              icon="💳"
              title="پرداختی ثبت نشده."
              description="برای این مشتری هنوز پرداختی در سیستم ثبت نشده است."
            />
          ) : (
            <Table>
              <thead>
                <tr>
                  {["تاریخ", "مبلغ", "روش", "شمارهٔ پیگیری", "بابت"].map((h) => (
                    <Th key={h}>{h}</Th>
                  ))}
                </tr>
              </thead>
              <tbody>
                {selected.payments.map((p) => (
                  <Tr key={p.id}>
                    <Td className="py-2.5">{toJalaliDisplay(p.paidOn)}</Td>
                    <Td className="py-2.5 font-bold">{money(p.amount)}</Td>
                    <Td className="py-2.5 text-(--ice-3)">{p.method}</Td>
                    <Td className="py-2.5 text-(--ice-3)">{p.referenceNo ?? "—"}</Td>
                    <Td className="py-2.5 text-(--ice-3)">{p.allocatedTo.join("، ")}</Td>
                  </Tr>
                ))}
              </tbody>
            </Table>
          )}
        </div>
      )}
    </div>
  );
}
