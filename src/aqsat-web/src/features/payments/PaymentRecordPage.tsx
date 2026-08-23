import { useState } from "react";
import { api, ApiError } from "../../lib/api";
import { fa, money } from "../../lib/persian";
import { RecordPaymentDialog } from "../today/RecordPaymentDialog";

interface CustomerListItemDto {
  id: string;
  fullName: string;
  mobile: string | null;
  policyCount: number;
}

interface OpenInstallmentDto {
  installmentId: string;
  policyNumber: string;
  insuranceLineNameFa: string;
  seqNo: number;
  dueDate: string;
  balance: number;
  status: "Unpaid" | "Partial";
}

const STATUS_LABEL: Record<OpenInstallmentDto["status"], string> = {
  Unpaid: "پرداخت‌نشده",
  Partial: "پرداخت جزئی",
};

export function PaymentRecordPage() {
  const [query, setQuery] = useState("");
  const [customers, setCustomers] = useState<CustomerListItemDto[] | null>(null);
  const [selected, setSelected] = useState<CustomerListItemDto | null>(null);
  const [installments, setInstallments] = useState<OpenInstallmentDto[] | null>(null);
  const [payingRow, setPayingRow] = useState<OpenInstallmentDto | null>(null);
  const [error, setError] = useState<string | null>(null);

  async function search() {
    if (!query.trim()) return;
    setError(null);
    setSelected(null);
    setInstallments(null);
    try {
      const results = await api.get<CustomerListItemDto[]>(`/customers?search=${encodeURIComponent(query.trim())}`);
      setCustomers(results);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "جست‌وجو ناموفق بود.");
    }
  }

  async function selectCustomer(customer: CustomerListItemDto) {
    setSelected(customer);
    setInstallments(null);
    setError(null);
    try {
      const open = await api.get<OpenInstallmentDto[]>(`/customers/${customer.id}/open-installments`);
      setInstallments(open);
    } catch (err) {
      setError(err instanceof ApiError ? err.message : "خطا در بارگذاری اقساط باز");
    }
  }

  function reloadInstallments() {
    if (selected) selectCustomer(selected);
  }

  return (
    <div>
      <h2 className="mb-1 text-xl font-extrabold tracking-tight text-(--ice)">
        ثبت پرداخت <em className="font-extralight not-italic text-(--ice-2)">مستقل</em>
      </h2>
      <div className="mb-4.5 text-xs text-(--ice-3)">جست‌وجوی مشتری و ثبت پرداخت بدون نیاز به عبور از داشبورد شمارش‌معکوس</div>

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
          placeholder="نام یا شمارهٔ همراه مشتری"
          className="flex-1 rounded-[10px] border border-(--edge-2) bg-(--fld) px-3 py-2 text-[13px] text-(--ice) outline-none focus:border-(--mint)"
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
        <div className="mb-4.5">
          <div className="mb-2 text-[11px] text-(--ice-3)">{fa(customers.length)} نتیجه</div>
          {customers.length === 0 ? (
            <div className="rounded-2xl border border-(--edge) bg-(--pane) p-6 text-center text-[13px] text-(--ice-3)">مشتری‌ای یافت نشد.</div>
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
              onClick={() => {
                setSelected(null);
                setInstallments(null);
              }}
              className="text-[11.5px] text-(--ice-3) underline underline-offset-2 hover:text-(--ice)"
            >
              بازگشت به نتایج جست‌وجو
            </button>
          </div>

          {installments === null ? (
            <div className="text-[12.5px] text-(--ice-3)">در حال بارگذاری…</div>
          ) : installments.length === 0 ? (
            <div className="rounded-2xl border border-(--edge) bg-(--pane) p-6 text-center text-[13px] text-(--ice-3)">این مشتری قسط بازی ندارد.</div>
          ) : (
            <div className="overflow-hidden rounded-2xl border border-(--edge) bg-(--pane)">
              <table className="w-full border-collapse">
                <thead>
                  <tr>
                    {["بیمه‌نامه", "رشته", "قسط", "سررسید", "مانده", "وضعیت", ""].map((h) => (
                      <th key={h} className="border-b border-(--edge) px-3 py-2.5 text-right text-[10.5px] font-medium tracking-wider text-(--ice-3)">
                        {h}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody>
                  {installments.map((i) => (
                    <tr key={i.installmentId} className="border-t border-(--edge) first:border-t-0">
                      <td className="px-3 py-2.5 text-[13px] font-semibold">{i.policyNumber}</td>
                      <td className="px-3 py-2.5 text-[13px] text-(--ice-3)">{i.insuranceLineNameFa}</td>
                      <td className="px-3 py-2.5 text-[13px]">{fa(i.seqNo)}</td>
                      <td className="px-3 py-2.5 text-[13px]">{fa(i.dueDate)}</td>
                      <td className="px-3 py-2.5 text-[13px] font-bold">{money(i.balance)}</td>
                      <td className="px-3 py-2.5 text-[13px] text-(--ice-3)">{STATUS_LABEL[i.status]}</td>
                      <td className="px-3 py-2.5 text-[13px]">
                        <button
                          type="button"
                          onClick={() => setPayingRow(i)}
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
        </div>
      )}

      {payingRow && selected && (
        <RecordPaymentDialog
          installmentId={payingRow.installmentId}
          customerFullName={selected.fullName}
          suggestedAmount={payingRow.balance}
          onClose={() => setPayingRow(null)}
          onRecorded={reloadInstallments}
        />
      )}
    </div>
  );
}
